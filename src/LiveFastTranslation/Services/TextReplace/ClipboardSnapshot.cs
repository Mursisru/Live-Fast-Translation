using System.Runtime.InteropServices;

namespace LiveFastTranslation.Services.TextReplace;

/// <summary>
/// Copies and restores the clipboard. All members must be used from an STA thread (the WPF UI thread).
/// </summary>
public sealed class ClipboardSnapshot
{
    private readonly System.Windows.IDataObject? _data;
    private readonly bool _hadData;

    private ClipboardSnapshot(System.Windows.IDataObject? data, bool hadData)
    {
        _data = data;
        _hadData = hadData;
    }

    public static ClipboardSnapshot Capture()
    {
        try
        {
            var source = System.Windows.Clipboard.GetDataObject();
            if (source is null)
            {
                return new ClipboardSnapshot(null, false);
            }

            var copy = new System.Windows.DataObject();
            var copiedAny = false;
            foreach (var format in source.GetFormats(autoConvert: false))
            {
                try
                {
                    var value = source.GetData(format, autoConvert: false);
                    if (value is null)
                    {
                        continue;
                    }

                    if (value is MemoryStream stream)
                    {
                        value = new MemoryStream(stream.ToArray());
                    }

                    copy.SetData(format, value);
                    copiedAny = true;
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException or NotSupportedException)
                {
                    // Some clipboard formats use handles that are only valid while the source object is alive.
                }
            }

            return new ClipboardSnapshot(copy, copiedAny);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            return new ClipboardSnapshot(null, false);
        }
    }

    public void Restore()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (_hadData && _data is not null)
                {
                    System.Windows.Clipboard.SetDataObject(_data, copy: true);
                }
                else
                {
                    System.Windows.Clipboard.Clear();
                }

                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(40);
            }
        }
    }

    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    public static bool TryGetText(out string text)
    {
        text = string.Empty;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    text = System.Windows.Clipboard.GetText();
                    return true;
                }

                return false;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}
