using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mouse2Joy.Engine;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Mouse2Joy.VirtualPad;

public sealed class ViGEmVirtualPad : IVirtualPad
{
    private readonly ILogger _logger;
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private readonly object _gate = new();
    private bool _firstConnectAttempted;

    public ViGEmVirtualPad(ILogger<ViGEmVirtualPad>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _controller is not null;
            }
        }
    }

    /// <summary>
    /// Construct the underlying <see cref="ViGEmClient"/> at app startup so the
    /// first-time COM/IOCTL init is paid before any user interaction. Nefarius.ViGEm.Client
    /// v1.21.256 occasionally surfaces a spurious exception with HResult 0 ("The
    /// operation completed successfully") on the very first connect of a process; warming
    /// the client up front sidesteps the user-visible failure on the first profile activation.
    /// Safe to call when ViGEmBus is missing — logs a warning and returns without throwing
    /// so the UI can still come up and the Setup tab can surface the issue.
    /// </summary>
    public void Prewarm()
    {
        lock (_gate)
        {
            if (_client is not null)
            {
                return;
            }

            try
            {
                _client = new ViGEmClient();
                _logger.LogInformation("ViGEm client pre-warmed at startup");
            }
            catch (VigemBusNotFoundException ex)
            {
                _logger.LogWarning(ex, "ViGEmBus driver not installed — pre-warm skipped");
            }
            catch (Exception ex) when (IsSpuriousSuccessException(ex))
            {
                _logger.LogInformation(ex, "ViGEm pre-warm hit the known HResult-0 quirk; will retry on first Connect");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ViGEm pre-warm failed; will retry on first Connect");
            }
        }
    }

    public void Connect()
    {
        lock (_gate)
        {
            if (_controller is not null)
            {
                return;
            }

            try
            {
                ConnectInternal();
            }
            catch (Exception ex) when (!_firstConnectAttempted && IsSpuriousSuccessException(ex))
            {
                _firstConnectAttempted = true;
                _logger.LogInformation(ex, "ViGEm Connect threw the known HResult-0 quirk on first attempt; retrying once");
                // Drop any partially-initialized state from the failed attempt.
                _controller = null;
                try { _client?.Dispose(); } catch { /* ignore */ }
                _client = null;
                Thread.Sleep(50);
                ConnectInternal();
                _logger.LogInformation("ViGEm Connect succeeded on retry");
                return;
            }
            _firstConnectAttempted = true;
        }
    }

    private void ConnectInternal()
    {
        _client ??= new ViGEmClient();
        var pad = _client.CreateXbox360Controller();
        // We submit explicitly per tick rather than letting the wrapper auto-flush
        // on every property setter — much less overhead at 250 Hz.
        pad.AutoSubmitReport = false;
        pad.Connect();
        _controller = pad;
        // Note: pad.UserIndex would be nice to log, but reading it immediately
        // after Connect() races with OS XInput-slot enumeration and throws
        // Xbox360UserIndexNotReportedException. We don't actually need it.
        _logger.LogInformation("ViGEm Xbox 360 pad connected");
    }

    // The library v1.21.256 occasionally throws an exception whose Win32 HResult is 0
    // (ERROR_SUCCESS) — the framework fills in the system message "The operation
    // completed successfully". We treat that exact signature as a benign first-init
    // hiccup; everything else propagates.
    private static bool IsSpuriousSuccessException(Exception ex)
    {
        if (ex is VigemBusNotFoundException)
        {
            return false;
        }

        if (ex.HResult == 0)
        {
            return true;
        }

        return ex.Message.Contains("operation completed successfully", StringComparison.OrdinalIgnoreCase);
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            if (_controller is null)
            {
                return;
            }

            try { _controller.Disconnect(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disconnect threw"); }
            _controller = null;
        }
    }

    public void Submit(in XInputReport report)
    {
        IXbox360Controller? c;
        lock (_gate)
        {
            c = _controller;
        }

        if (c is null)
        {
            return;
        }

        c.SetAxisValue(Xbox360Axis.LeftThumbX, report.LeftThumbX);
        c.SetAxisValue(Xbox360Axis.LeftThumbY, report.LeftThumbY);
        c.SetAxisValue(Xbox360Axis.RightThumbX, report.RightThumbX);
        c.SetAxisValue(Xbox360Axis.RightThumbY, report.RightThumbY);
        c.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
        c.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);
        // The wButton bitmask we computed is identical to ViGEm's mask values
        // (verified: see Xbox360Button static fields), so pass it through.
        c.SetButtonsFull((ushort)report.Buttons);
        c.SubmitReport();
    }

    public void Dispose()
    {
        Disconnect();
        try { _client?.Dispose(); } catch { /* ignore */ }
        _client = null;
    }
}
