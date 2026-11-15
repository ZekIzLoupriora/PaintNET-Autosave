using System.Collections;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoSavePdnPoc;

internal static class AutoSaveCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<object, WorkspaceState> States = new(ReferenceEqualityComparer.Instance);
    private static IServiceProvider? services;
    private static AutoSaveSettings settings = AutoSaveSettings.Current;
    private static SynchronizationContext? uiContext;
    private static System.Threading.Timer? discoveryTimer;
    private static bool initialized;
    private static Type? appWorkspaceType;
    private static object? appWorkspace;
    private static ToolStripStatusLabel? statusIndicator;
    private static bool historyControlHooked;
    private static object? lastHistoryStack;

    public static void CaptureUiContext()
    {
        lock (Sync)
        {
            uiContext ??= SynchronizationContext.Current;
        }
    }

    public static void Start(IServiceProvider effectServices, AutoSaveSettings newSettings)
    {
        lock (Sync)
        {
            services = effectServices;
            settings = newSettings;
            uiContext ??= SynchronizationContext.Current;

            if (initialized)
            {
                foreach (var state in States.Values)
                    state.ApplySettings();
                PostToUi(UpdateStatusIndicator);
                return;
            }

            initialized = true;
            if (uiContext is not null)
            {
                uiContext.Post(_ => InitializeOnUiThread(), null);
            }
            else
            {
                InitializeOnUiThread();
            }
        }
    }

    private static void InitializeOnUiThread()
    {
        try
        {
            appWorkspaceType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetTypesSafely)
                .FirstOrDefault(type => type.FullName == "PaintDotNet.Controls.AppWorkspace");

            if (appWorkspaceType is null)
                throw new InvalidOperationException("Paint.NET AppWorkspace type was not found.");

            // AppWorkspace is the main Paint.NET window. It is not registered
            // in the effect IServiceProvider, so resolve it from OpenForms
            // first, with the service provider retained as a compatibility
            // fallback for hosts that do register it.
            appWorkspace = Application.OpenForms
                .Cast<Form>()
                .Select(form => FindControl(form, appWorkspaceType))
                .FirstOrDefault(control => control is not null);
            appWorkspace ??= services?.GetService(appWorkspaceType);
            if (appWorkspace is null)
                throw new InvalidOperationException("Paint.NET did not expose AppWorkspace through the effect services.");

            InstallStatusIndicator();
            InstallHistoryControlHook();
            ScanWorkspaces();
            discoveryTimer = new System.Threading.Timer(_ => PostToUi(ScanWorkspaces), null, 500, 500);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("coordinator-error.txt", ex);
        }
    }

    private static void ScanWorkspaces()
    {
        try
        {
            if (appWorkspace is null)
                return;

            InstallHistoryControlHook();

            var property = appWorkspace.GetType().GetProperty("DocumentWorkspaces", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("AppWorkspace.DocumentWorkspaces was not found.");

            if (property.GetValue(appWorkspace) is not IEnumerable workspaces)
                return;

            foreach (var workspace in workspaces)
            {
                if (workspace is not null && !States.ContainsKey(workspace))
                    HookWorkspace(workspace);
            }

            ProcessTimeSaves();
            UpdateStatusIndicator();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("coordinator-error.txt", ex);
        }
    }

    private static void InstallHistoryControlHook()
    {
        if (historyControlHooked)
            return;

        var historyControl = Application.OpenForms
            .Cast<Form>()
            .Select(form => FindControlByName(form, "historyControl"))
            .FirstOrDefault(control => control is not null);
        if (historyControl is null)
            return;

        lastHistoryStack = historyControl.GetType()
            .GetProperty("HistoryStack", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(historyControl);

        var historyChanged = historyControl.GetType().GetEvent("HistoryChanged", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Paint.NET HistoryControl.HistoryChanged was not found.");
        historyChanged.AddEventHandler(historyControl, new EventHandler(OnHistoryControlChanged));
        historyControlHooked = true;
        WriteTrace("Hooked HistoryControl.HistoryChanged.");
    }

    private static void OnHistoryControlChanged(object? sender, EventArgs e)
    {
        if (sender is null)
            return;

        var history = sender.GetType().GetProperty("HistoryStack", BindingFlags.Public | BindingFlags.Instance)?.GetValue(sender);
        if (!ReferenceEquals(history, lastHistoryStack))
        {
            lastHistoryStack = history;
            WriteTrace("HistoryControl switched documents; counter not incremented.");
            UpdateStatusIndicator();
            return;
        }

        var state = States.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.History, history));
        if (state is null)
        {
            WriteTrace("HistoryChanged received, but no matching workspace was found.");
            return;
        }

        state.OnHistoryControlChanged();
    }

    private static void HookWorkspace(object workspace)
    {
        var history = workspace.GetType().GetProperty("History", BindingFlags.Public | BindingFlags.Instance)?.GetValue(workspace)
            ?? throw new InvalidOperationException("DocumentWorkspace.History was not found.");

        var state = new WorkspaceState(workspace, history);
        var historyType = history.GetType();
        historyType.GetEvent("NewHistoryMemento")?.AddEventHandler(history, new EventHandler(state.OnNewHistoryMemento));
        historyType.GetEvent("HistoryFlushed")?.AddEventHandler(history, new EventHandler(state.OnHistoryFlushed));

        var documentChanged = workspace.GetType().GetEvent("DocumentChanged", BindingFlags.Public | BindingFlags.Instance);
        documentChanged?.AddEventHandler(workspace, new EventHandler(state.OnDocumentChanged));
        States.Add(workspace, state);
        WriteTrace($"Hooked workspace {workspace.GetHashCode()}.");
    }

    private static void ProcessTimeSaves()
    {
        if (!settings.IntervalMinutesEnabled)
            return;

        var now = DateTime.UtcNow;
        foreach (var state in States.Values.ToArray())
            state.TryTimeSave(now);
    }

    private static void PostToUi(Action action)
    {
        if (uiContext is not null)
            uiContext.Post(_ => action(), null);
        else
            action();
    }

    private static void InstallStatusIndicator()
    {
        if (appWorkspace is not Control workspaceControl)
            throw new InvalidOperationException("AppWorkspace is not a Windows Forms control.");

        var statusBar = FindControlByName(workspaceControl, "statusBar") as StatusStrip;
        if (statusBar is null)
            throw new InvalidOperationException("Paint.NET status bar was not found.");

        statusIndicator = statusBar.Items
            .OfType<ToolStripStatusLabel>()
            .FirstOrDefault(item => item.Name == "AutoSaveStatusIndicator");

        if (statusIndicator is null)
        {
            statusIndicator = new ToolStripStatusLabel
            {
                Name = "AutoSaveStatusIndicator",
                AutoSize = true,
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                BorderStyle = Border3DStyle.Etched,
                Padding = new Padding(8, 0, 4, 0),
                ToolTipText = "Auto-Save status",
            };
            statusBar.Items.Add(statusIndicator);
        }

        UpdateStatusIndicator();
    }

    private static void UpdateStatusIndicator()
    {
        if (statusIndicator is null)
            return;

        var activeWorkspace = appWorkspace?.GetType()
            .GetProperty("ActiveDocumentWorkspace", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(appWorkspace);
        WorkspaceState? activeState = null;
        if (activeWorkspace is not null)
            States.TryGetValue(activeWorkspace, out activeState);

        string? historyText = null;
        string? timeText = null;
        if (settings.ChangeCountEnabled)
        {
            var current = activeState?.ChangeCount ?? 0;
            historyText = $"{current}/{settings.ChangeCount}";
        }
        if (settings.IntervalMinutesEnabled)
        {
            var remaining = activeState?.GetRemainingTime(DateTime.UtcNow)
                ?? TimeSpan.FromMinutes(settings.IntervalMinutes);
            var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            timeText = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }

        statusIndicator.Text = historyText is null && timeText is null
            ? "Auto-Save: Off"
            : $"Auto-Save: {string.Join(", ", new[] { historyText, timeText }.Where(value => value is not null))}";
        statusIndicator.ForeColor = historyText is null && timeText is null
            ? System.Drawing.SystemColors.GrayText
            : System.Drawing.SystemColors.ControlText;
    }

    private static Control? FindControlByName(Control parent, string name)
    {
        if (string.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase))
            return parent;

        foreach (Control child in parent.Controls)
        {
            var match = FindControlByName(child, name);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static Control? FindControl(Control parent, Type targetType)
    {
        if (targetType.IsInstanceOfType(parent))
            return parent;

        foreach (Control child in parent.Controls)
        {
            var match = FindControl(child, targetType);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type is not null)!; }
    }

    private static void WriteDiagnostic(string fileName, Exception ex)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paint.NET", "AutoSavePdnPoc");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, fileName), $"{DateTime.Now:O}\r\n{ex}\r\n\r\n");
        }
        catch { }
    }

    private static void WriteTrace(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paint.NET", "AutoSavePdnPoc");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "coordinator-trace.txt"), $"{DateTime.Now:O} {message}\r\n");
        }
        catch { }
    }

    private sealed class WorkspaceState
    {
        private readonly object workspace;
        public object History { get; }
        private int changeCount;
        private DateTime nextTimeSaveUtc;
        public int ChangeCount => changeCount;

        public WorkspaceState(object workspace, object history)
        {
            this.workspace = workspace;
            History = history;
            ApplySettings();
        }

        public void ApplySettings()
        {
            nextTimeSaveUtc = settings.IntervalMinutesEnabled
                ? DateTime.UtcNow.AddMinutes(settings.IntervalMinutes)
                : DateTime.MaxValue;
        }

        public TimeSpan GetRemainingTime(DateTime nowUtc)
        {
            if (!settings.IntervalMinutesEnabled || nextTimeSaveUtc == DateTime.MaxValue)
                return TimeSpan.Zero;
            return nextTimeSaveUtc > nowUtc ? nextTimeSaveUtc - nowUtc : TimeSpan.Zero;
        }

        public void OnNewHistoryMemento(object? sender, EventArgs e)
        {
            WriteTrace($"NewHistoryMemento workspace={workspace.GetHashCode()}.");
        }

        public void OnHistoryControlChanged()
        {
            changeCount++;
            WriteTrace($"HistoryChanged workspace={workspace.GetHashCode()} count={changeCount}.");
            UpdateStatusIndicator();
            TrySaveIfThresholdReached();
        }

        public void TryTimeSave(DateTime nowUtc)
        {
            if (!settings.IntervalMinutesEnabled || nextTimeSaveUtc > nowUtc)
                return;

            try
            {
                SaveSnapshot();
                nextTimeSaveUtc = nowUtc.AddMinutes(settings.IntervalMinutes);
                WriteTrace($"Time snapshot requested workspace={workspace.GetHashCode()}.");
            }
            catch (Exception ex)
            {
                WriteDiagnostic("coordinator-error.txt", ex);
                nextTimeSaveUtc = nowUtc.AddMinutes(settings.IntervalMinutes);
            }
        }

        private void TrySaveIfThresholdReached()
        {
            var currentSettings = settings;
            if (!currentSettings.ChangeCountEnabled || changeCount < currentSettings.ChangeCount)
                return;

            try
            {
                SaveSnapshot();
                WriteTrace($"Snapshot requested workspace={workspace.GetHashCode()}.");
                changeCount = 0;
                UpdateStatusIndicator();
            }
            catch (Exception ex)
            {
                WriteDiagnostic("coordinator-error.txt", ex);
            }
        }

        private void SaveSnapshot()
        {
            var document = workspace.GetType().GetProperty("Document")?.GetValue(workspace)
                ?? throw new InvalidOperationException("DocumentWorkspace.Document was not found.");
            var filePath = workspace.GetType().GetProperty("FilePath")?.GetValue(workspace) as string;
            SnapshotExporter.TryExport(document, filePath);
        }

        public void OnHistoryFlushed(object? sender, EventArgs e)
        {
            changeCount = 0;
            UpdateStatusIndicator();
        }

        public void OnDocumentChanged(object? sender, EventArgs e)
        {
            changeCount = 0;
            UpdateStatusIndicator();
        }
    }
}
