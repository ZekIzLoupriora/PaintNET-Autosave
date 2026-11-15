using PaintDotNet.Effects;
using System.Windows.Forms;

namespace AutoSavePdnPoc;

// Paint.NET discovers BitmapEffect subclasses from an effect DLL. Invoking this
// effect once opens the configuration dialog and applies the saved settings.
public sealed class BootstrapEffect : BitmapEffect
{
    public BootstrapEffect()
        : base("Auto-Save PoC (snapshot)", "Auto-Save", CreateOptions())
    {
    }

    private static BitmapEffectOptions CreateOptions()
    {
        var options = BitmapEffectOptions.Create();
        options.IsConfigurable = true;
        return options;
    }

    protected override IEffectConfigForm OnCreateConfigForm()
    {
        return new SaveConfigForm();
    }

    protected override void OnInitializeRenderInfo(IBitmapEffectRenderInfo renderInfo)
    {
        renderInfo.Flags = BitmapEffectRenderingFlags.DisableSelectionClipping |
                           BitmapEffectRenderingFlags.SingleThreaded;
    }

    protected override void OnRender(IBitmapEffectOutput output)
    {
        if (AutoSaveSettings.ActivationRequested)
        {
            AutoSaveSettings.ActivationRequested = false;
            AutoSaveCoordinator.Start(Services, AutoSaveSettings.Current);
        }
        CopySourceLayerToOutput(output);
    }

    private void CopySourceLayerToOutput(IBitmapEffectOutput output)
    {
        var source = Environment.Document.Layers[Environment.SourceLayerIndex].GetBitmapBgra32();
        using var sourceLock = (PaintDotNet.Imaging.IBitmapLock<PaintDotNet.Imaging.ColorBgra32>)source.Lock(output.Bounds);
        using var outputLock = output.LockBgra32();
        PaintDotNet.Imaging.BitmapLockExtensions.AsRegionPtr(sourceLock)
            .CopyTo(PaintDotNet.Imaging.BitmapLockExtensions.AsRegionPtr(outputLock));
    }
}

internal sealed class SaveConfigForm : EffectConfigForm2
{
    private readonly NumericUpDown changesNumeric;
    private readonly CheckBox changesEnabledCheckBox;
    private readonly NumericUpDown minutesNumeric;
    private readonly CheckBox minutesEnabledCheckBox;
    private readonly Label statusLabel;
    private readonly Button saveButton;

    public SaveConfigForm()
    {
        Text = "Auto-Save";
        ClientSize = new System.Drawing.Size(430, 154);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var changesLabel = new Label
        {
            Text = "Save every N changes",
            AutoSize = true,
            Location = new System.Drawing.Point(12, 16),
        };

        changesNumeric = CreateNumericControl(10, 150, 12);
        changesEnabledCheckBox = CreateEnableCheckBox(235, 12);

        var minutesLabel = new Label
        {
            Text = "Save every N minutes",
            AutoSize = true,
            Location = new System.Drawing.Point(12, 54),
        };

        minutesNumeric = CreateNumericControl(5, 150, 50);
        minutesEnabledCheckBox = CreateEnableCheckBox(235, 50);

        statusLabel = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(12, 94),
        };

        saveButton = new Button
        {
            Text = "Save config and enable",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new System.Drawing.Point(345, 112),
        };

        Controls.Add(changesLabel);
        Controls.Add(changesNumeric);
        Controls.Add(changesEnabledCheckBox);
        Controls.Add(minutesLabel);
        Controls.Add(minutesNumeric);
        Controls.Add(minutesEnabledCheckBox);
        Controls.Add(statusLabel);
        Controls.Add(saveButton);
        AcceptButton = saveButton;

        saveButton.Click += (_, _) => PersistCurrentSettings();
        changesEnabledCheckBox.CheckedChanged += (_, _) => UpdateStatusLabel();
        minutesEnabledCheckBox.CheckedChanged += (_, _) => UpdateStatusLabel();
        UpdateStatusLabel();
    }

    protected override EffectConfigToken OnCreateInitialToken()
    {
        return new AutoSaveConfigToken();
    }

    protected override void OnUpdateDialogFromToken(EffectConfigToken token)
    {
        // Paint.NET may recreate the effect token with its own defaults when
        // the effect is opened again. The persisted settings are authoritative.
        var settings = AutoSaveSettings.Current;
        changesNumeric.Value = settings.ChangeCount;
        changesEnabledCheckBox.Checked = settings.ChangeCountEnabled;
        minutesNumeric.Value = settings.IntervalMinutes;
        minutesEnabledCheckBox.Checked = settings.IntervalMinutesEnabled;
        UpdateStatusLabel();
    }

    protected override void OnUpdateTokenFromDialog(EffectConfigToken token)
    {
        AutoSaveCoordinator.CaptureUiContext();
        var settings = (AutoSaveConfigToken)token;
        settings.ChangeCount = (int)changesNumeric.Value;
        settings.ChangeCountEnabled = changesEnabledCheckBox.Checked;
        settings.IntervalMinutes = (int)minutesNumeric.Value;
        settings.IntervalMinutesEnabled = minutesEnabledCheckBox.Checked;
    }

    private static NumericUpDown CreateNumericControl(int value, int x, int top)
    {
        return new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999999,
            Value = value,
            Width = 70,
            Location = new System.Drawing.Point(x, top),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
    }

    private static CheckBox CreateEnableCheckBox(int x, int top)
    {
        return new CheckBox
        {
            Text = "Enable",
            AutoSize = true,
            Location = new System.Drawing.Point(x, top + 2),
        };
    }

    private void UpdateStatusLabel()
    {
        var modes = new List<string>();
        if (changesEnabledCheckBox.Checked) modes.Add("History");
        if (minutesEnabledCheckBox.Checked) modes.Add("time");
        statusLabel.Text = modes.Count == 0
            ? "Status: Disabled"
            : $"Status: Enabled ({string.Join(" + ", modes)})";
    }

    private void PersistCurrentSettings()
    {
        AutoSaveSettings.Current = new AutoSaveSettings(
            (int)changesNumeric.Value,
            changesEnabledCheckBox.Checked,
            (int)minutesNumeric.Value,
            minutesEnabledCheckBox.Checked);
        AutoSaveSettings.ActivationRequested = true;
        AutoSaveSettings.Save();
    }

}

internal sealed class AutoSaveConfigToken : EffectConfigToken
{
    public int ChangeCount { get; set; }
    public bool ChangeCountEnabled { get; set; }
    public int IntervalMinutes { get; set; }
    public bool IntervalMinutesEnabled { get; set; }

    public AutoSaveConfigToken()
    {
        var settings = AutoSaveSettings.Current;
        ChangeCount = settings.ChangeCount;
        ChangeCountEnabled = settings.ChangeCountEnabled;
        IntervalMinutes = settings.IntervalMinutes;
        IntervalMinutesEnabled = settings.IntervalMinutesEnabled;
    }

    public override object Clone() => new AutoSaveConfigToken
    {
        ChangeCount = ChangeCount,
        ChangeCountEnabled = ChangeCountEnabled,
        IntervalMinutes = IntervalMinutes,
        IntervalMinutesEnabled = IntervalMinutesEnabled,
    };
}

internal sealed record AutoSaveSettings(
    int ChangeCount,
    bool ChangeCountEnabled,
    int IntervalMinutes,
    bool IntervalMinutesEnabled)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paint.NET",
        "AutoSavePdnPoc",
        "settings.json");

    public static AutoSaveSettings Current { get; set; } = Load();
    public static bool ActivationRequested { get; set; }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(Current, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch
        {
            // Settings persistence must never prevent the effect from running.
        }
    }

    private static AutoSaveSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<AutoSaveSettings>(File.ReadAllText(SettingsPath));
                if (saved is not null && saved.ChangeCount >= 1 && saved.IntervalMinutes >= 1)
                    return saved;
            }
        }
        catch { }

        return new AutoSaveSettings(10, false, 5, false);
    }
}
