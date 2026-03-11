using System.Buffers;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nesti.Helpers;

/// <summary>
/// Plays a GIF animation with correct compositing and minimal fixed RAM.
///
/// DESIGN
/// ──────
/// • One MemoryStream per GIF  : compressed bytes only (~50–500 KB).
/// • BitmapCacheOption.None    : decoder does not pre-cache all decoded frames.
///                               Each Frames[i] access decodes on demand from the stream.
/// • _canvas byte[]            : a persistent BGRA buffer that accumulates frame
///                               compositions (the "logical screen").  O(1 frame) size.
/// • SharedBitmap              : one WriteableBitmap all Image controls share.
///                               Updated each tick from _canvas.
/// • No List&lt;BitmapSource&gt;   : zero per-notification frame storage.
/// • ArrayPool for temp pixels : avoids GC pressure each frame tick.
///
/// GIF compositing handled:
///   - Per-frame Left/Top offsets (sub-rectangle frames)
///   - Binary transparency (alpha = 0 → leave canvas pixel unchanged)
///   - Disposal 0/1 (leave in place)
///   - Disposal 2 (restore area to transparent before next frame)
///   - Disposal 3 (restore canvas to state before current frame was drawn)
/// </summary>
public sealed class SharedGifPlayer
{
    // ── Cache ─────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, SharedGifPlayer> _cache = new();

    public static SharedGifPlayer Get(string packUri)
    {
        if (!_cache.TryGetValue(packUri, out var p))
            _cache[packUri] = p = new SharedGifPlayer(packUri);
        return p;
    }

    // ── Per-frame metadata (read once at startup, then only ints in memory) ──
    private readonly record struct FrameInfo(
        int Left, int Top, int W, int H, int DelayMs, int Disposal);

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly GifBitmapDecoder _decoder;
    private readonly DispatcherTimer  _timer;
    private readonly FrameInfo[]      _infos;
    private readonly int              _frameCount;
    private readonly byte[]           _canvas;       // persistent BGRA composite canvas
    private readonly int              _canvasW;
    private readonly int              _canvasH;
    private readonly int              _canvasStride;
    private          byte[]?          _savedCanvas;  // only used for disposal = 3
    private          int              _index;

    /// <summary>
    /// Set Image.Source to this once.  WriteableBitmap auto-notifies WPF every time
    /// pixels change — no event subscription needed per Image control.
    /// </summary>
    public WriteableBitmap SharedBitmap { get; }

    // ── Constructor ───────────────────────────────────────────────────────────
    private SharedGifPlayer(string packUri)
    {
        // 1. Load compressed GIF bytes into a seekable MemoryStream.
        //    The decoder needs to seek back to re-read frames on demand.
        var ms = new MemoryStream();
        using (var s = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))!.Stream)
            s.CopyTo(ms);
        ms.Position = 0;

        // 2. BitmapCacheOption.None → no decoded pixels cached inside the decoder.
        _decoder    = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        _frameCount = _decoder.Frames.Count;

        // 3. Read GIF canvas (logical screen) size from global metadata.
        try
        {
            var gm = (BitmapMetadata)_decoder.Metadata;
            _canvasW = (ushort)gm.GetQuery("/logscrdesc/Width");
            _canvasH = (ushort)gm.GetQuery("/logscrdesc/Height");
        }
        catch
        {
            _canvasW = _decoder.Frames[0].PixelWidth;
            _canvasH = _decoder.Frames[0].PixelHeight;
        }

        // 4. Allocate the persistent logical canvas (BGRA, starts fully transparent).
        _canvasStride = _canvasW * 4;
        _canvas       = new byte[_canvasH * _canvasStride];

        // 5. Allocate the shared display surface.
        SharedBitmap = new WriteableBitmap(_canvasW, _canvasH, 96, 96, PixelFormats.Bgra32, null);

        // 6. Read per-frame metadata (header bytes only — no pixel decoding here).
        _infos = new FrameInfo[_frameCount];
        for (int i = 0; i < _frameCount; i++)
        {
            var frame = _decoder.Frames[i];
            var meta  = (BitmapMetadata)frame.Metadata;
            int left = 0, top = 0, fw = frame.PixelWidth, fh = frame.PixelHeight,
                delay = 100, disposal = 0;
            try { left     = (ushort)meta.GetQuery("/imgdesc/Left");         } catch { }
            try { top      = (ushort)meta.GetQuery("/imgdesc/Top");          } catch { }
            try { fw       = (ushort)meta.GetQuery("/imgdesc/Width");        } catch { }
            try { fh       = (ushort)meta.GetQuery("/imgdesc/Height");       } catch { }
            try { var d    = (ushort)meta.GetQuery("/grctlext/Delay");
                  delay    = Math.Max(20, d * 10);                           } catch { }
            try { disposal = (byte)meta.GetQuery("/grctlext/Disposal");      } catch { }
            _infos[i] = new FrameInfo(left, top, fw, fh, delay, disposal);
        }

        // 7. Composite and display frame 0.
        CompositeFrame(0);

        // 8. Start animation timer.
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        if (_frameCount > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_infos[0].DelayMs);
            _timer.Tick    += Advance;
            _timer.Start();
        }
    }

    /// <summary>
    /// Suspends animation (e.g. when the hosting Image is not visible).
    /// No pixels are written to the WriteableBitmap while paused.
    /// </summary>
    public void Pause()  => _timer.Stop();

    /// <summary>Resumes animation after a Pause().</summary>
    public void Resume() { if (_frameCount > 1) _timer.Start(); }

    /// <summary>
    /// Stops the animation timer and releases resources for this player instance.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Advance;
        System.Diagnostics.Debug.WriteLine($"[Nesti] SharedGifPlayer disposed");
    }

    /// <summary>
    /// Disposes all cached GIF player instances and clears the cache.
    /// Called on application exit to prevent timer leaks.
    /// </summary>
    public static void ClearAll()
    {
        foreach (var player in _cache.Values)
            player.Dispose();
        _cache.Clear();
        System.Diagnostics.Debug.WriteLine("[Nesti] SharedGifPlayer.ClearAll: all instances disposed");
    }

    // ── Animation loop ────────────────────────────────────────────────────────
    private void Advance(object? sender, EventArgs e)
    {
        // Apply the disposal method declared by the frame we just showed.
        ApplyDisposal(_index);

        // Move to next frame.
        _index = (_index + 1) % _frameCount;
        _timer.Interval = TimeSpan.FromMilliseconds(_infos[_index].DelayMs);
        CompositeFrame(_index);
    }

    // ── GIF compositing ───────────────────────────────────────────────────────

    private void ApplyDisposal(int i)
    {
        switch (_infos[i].Disposal)
        {
            case 2:
                // Restore the frame area to transparent before the next frame draws.
                ClearRect(_infos[i].Left, _infos[i].Top, _infos[i].W, _infos[i].H);
                break;
            case 3 when _savedCanvas is not null:
                // Restore the canvas to its state before this frame was drawn.
                Buffer.BlockCopy(_savedCanvas, 0, _canvas, 0, _canvas.Length);
                break;
        }
        _savedCanvas = null;
    }

    private void CompositeFrame(int i)
    {
        var fi = _infos[i];

        // For disposal=3, snapshot the canvas before modifying it.
        if (fi.Disposal == 3)
        {
            _savedCanvas ??= new byte[_canvas.Length];
            Buffer.BlockCopy(_canvas, 0, _savedCanvas, 0, _canvas.Length);
        }

        // Decode current frame's pixels (on demand — not cached by the decoder).
        var frame = _decoder.Frames[i];
        var src   = frame.Format == PixelFormats.Bgra32
            ? (BitmapSource)frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int fw = Math.Min(fi.W, _canvasW - fi.Left);
        int fh = Math.Min(fi.H, _canvasH - fi.Top);
        if (fw <= 0 || fh <= 0) return;

        // Borrow a temporary pixel buffer from the pool (avoid GC each tick).
        int   frameStride = fw * 4;
        byte[] buf        = ArrayPool<byte>.Shared.Rent(fh * frameStride);
        try
        {
            src.CopyPixels(new Int32Rect(0, 0, fw, fh), buf, frameStride, 0);
            BlendIntoCanvas(buf, fi.Left, fi.Top, fw, fh, frameStride);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        // Push the updated canvas to the shared display surface.
        SharedBitmap.WritePixels(
            new Int32Rect(0, 0, _canvasW, _canvasH),
            _canvas, _canvasStride, 0);
    }

    /// <summary>
    /// Blends frame pixels into the persistent canvas using GIF binary transparency:
    ///   alpha == 0  → transparent, keep existing canvas pixel
    ///   alpha  > 0  → opaque, overwrite canvas pixel
    /// </summary>
    private void BlendIntoCanvas(byte[] frameBuf, int dstX, int dstY, int fw, int fh, int frameStride)
    {
        for (int y = 0; y < fh; y++)
        {
            int cy = dstY + y;
            if ((uint)cy >= (uint)_canvasH) continue;

            int srcRow = y * frameStride;
            int dstRow = cy * _canvasStride;

            for (int x = 0; x < fw; x++)
            {
                int cx = dstX + x;
                if ((uint)cx >= (uint)_canvasW) continue;

                int si = srcRow + x * 4;
                if (frameBuf[si + 3] == 0) continue;   // transparent — leave canvas

                int di = dstRow + cx * 4;
                _canvas[di]     = frameBuf[si];
                _canvas[di + 1] = frameBuf[si + 1];
                _canvas[di + 2] = frameBuf[si + 2];
                _canvas[di + 3] = frameBuf[si + 3];
            }
        }
    }

    private void ClearRect(int x, int y, int w, int h)
    {
        int x2 = Math.Min(x + w, _canvasW);
        int y2 = Math.Min(y + h, _canvasH);
        for (int row = Math.Max(y, 0); row < y2; row++)
        {
            int off = row * _canvasStride + Math.Max(x, 0) * 4;
            int len = (x2 - Math.Max(x, 0)) * 4;
            Array.Clear(_canvas, off, len);
        }
    }
}
