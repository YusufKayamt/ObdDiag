using ObdDiag.Hardware;

namespace ObdDiag.App.ViewModels;

/// <summary>Sahte cihaz senaryosunun ekranda görünen adı.</summary>
public sealed record ScenarioOption(string Title, FakeScenario Value);
