namespace MathFirst.App.Components.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
#if MATHFIRST_TESTER_DIAGNOSTICS
using System;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Telemetry;
using MathFirst.App.Services;
#endif

public sealed class TesterDiagnosticsSection : ComponentBase
{
#if MATHFIRST_TESTER_DIAGNOSTICS
    [Inject]
    private ILocalizationService Localizer { get; set; } = default!;

    [Inject]
    private AppBuildInfo BuildInfo { get; set; } = default!;

    [Inject]
    private IAppPlatformInfo PlatformInfo { get; set; } = default!;

    [Inject]
    private IClipboardService ClipboardService { get; set; } = default!;

    [Inject]
    private TelemetryExportCoordinator TelemetryCoordinator { get; set; } = default!;

    private bool _isExporting;
    private string? _diagnosticMessage;
    private bool _diagnosticMessageIsError;
    private string? _telemetryMessage;
    private bool _telemetryMessageIsError;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "settings-diagnostics-action");

        // Copy Diagnostics button
        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "type", "button");
        builder.AddAttribute(4, "class", "button button-secondary button-compact settings-copy-diagnostics-btn");
        builder.AddAttribute(5, "onclick", EventCallback.Factory.Create(this, CopyDiagnosticsAsync));
        builder.AddContent(6, Localizer["Settings_CopyDiagnostics"]);
        builder.CloseElement();

        // Export Telemetry button
        builder.OpenElement(7, "button");
        builder.AddAttribute(8, "type", "button");
        builder.AddAttribute(9, "class", "button button-secondary button-compact settings-export-telemetry-btn");
        builder.AddAttribute(10, "disabled", _isExporting);
        builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, ExportTelemetryAsync));
        builder.AddContent(12, _isExporting ? Localizer["Settings_ExportTelemetry_InProgress"] : Localizer["Settings_ExportTelemetry"]);
        builder.CloseElement();

        builder.CloseElement();

        if (!string.IsNullOrEmpty(_diagnosticMessage))
        {
            builder.OpenElement(13, "p");
            builder.AddAttribute(14, "class", "setting-feedback " + (_diagnosticMessageIsError ? "setting-feedback-error" : "setting-feedback-success"));
            builder.AddAttribute(15, "role", "status");
            builder.AddContent(16, _diagnosticMessage);
            builder.CloseElement();
        }

        if (!string.IsNullOrEmpty(_telemetryMessage))
        {
            builder.OpenElement(17, "p");
            builder.AddAttribute(18, "class", "setting-feedback " + (_telemetryMessageIsError ? "setting-feedback-error" : "setting-feedback-success"));
            builder.AddAttribute(19, "role", "status");
            builder.AddContent(20, _telemetryMessage);
            builder.CloseElement();
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        _telemetryMessage = null;

        try
        {
            var diagnosticText = AppDiagnosticFormatter.Format(
                BuildInfo.ApplicationTitle,
                BuildInfo.DisplayVersion,
                BuildInfo.BuildNumber,
                BuildInfo.BuildClassification,
                BuildInfo.ShortSourceCommit,
                PlatformInfo.PlatformDescription,
                BuildInfo.ApplicationId);

            await ClipboardService.SetTextAsync(diagnosticText);
            _diagnosticMessage = Localizer["Settings_CopyDiagnostics_Success"];
            _diagnosticMessageIsError = false;
        }
        catch
        {
            _diagnosticMessage = Localizer["Settings_CopyDiagnostics_Failure"];
            _diagnosticMessageIsError = true;
        }
    }

    private async Task ExportTelemetryAsync()
    {
        if (_isExporting)
        {
            return;
        }

        _isExporting = true;
        _telemetryMessage = null;
        _diagnosticMessage = null;

        try
        {
            var result = await TelemetryCoordinator.ExportAndShareAsync();
            if (result.Success)
            {
                _telemetryMessage = Localizer["Settings_ExportTelemetry_Success"];
                _telemetryMessageIsError = false;
            }
            else
            {
                _telemetryMessage = Localizer["Settings_ExportTelemetry_Failure"];
                _telemetryMessageIsError = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _telemetryMessage = Localizer["Settings_ExportTelemetry_Failure"];
            _telemetryMessageIsError = true;
        }
        finally
        {
            _isExporting = false;
        }
    }
#endif
}
