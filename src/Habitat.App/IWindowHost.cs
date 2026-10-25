namespace Habitat;

internal interface IWindowHost
{
    bool IsActive { get; }
    nint Handle { get; }
    void SetBackgroundAppearance(double percent, bool dark);
    void SetStatusPresentation(string text, string status);
    bool DetectBackdropIsDark(bool current);
    void CloseForUpdate() { }
}
