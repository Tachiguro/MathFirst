namespace MathFirst.App.Services;

using MathFirst.Application;
using Microsoft.Maui.ApplicationModel.DataTransfer;

public sealed class MauiClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
}
