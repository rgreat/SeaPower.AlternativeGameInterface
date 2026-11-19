namespace AlternativeGameInterface
{
    /// <summary>
    /// The gear of the World View window (the 3D picture) opens the settings window of the mod used last (SettingsHub.OpenLastOther: another active mod, e.g. Alternative
    /// Special Effects); when the last one is this mod, or none, it opens our own. Every settings window marks its mod as the last one when it is built (kit ModRow ->
    /// SettingsHub.MarkLast), so the menu button and the gear agree.
    /// </summary>
    public partial class AlternativeGameInterfacePlugin
    {
        const string OwnHubId = "alternative-game-interface";
    }
}
