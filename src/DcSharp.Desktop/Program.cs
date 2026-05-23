using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace DcSharp.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const int DefaultFramebufferWidth = 320;
    private const int DefaultFramebufferHeight = 240;
    private const ulong DefaultInstructionLimit = 70_000_000;

    private readonly ToolStrip toolStrip = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripButton runButton = new("Run");
    private readonly ToolStripButton runDemoButton = new("Run Demo");
    private readonly ToolStripButton clearButton = new("Clear");
    private readonly TextBox elfPath = new();
    private readonly TextBox mediaPath = new();
    private readonly Button browseElfButton = new() { Text = "Browse..." };
    private readonly Button browseMediaButton = new() { Text = "Browse..." };
    private readonly Button clearMediaButton = new() { Text = "Clear" };
    private readonly NumericUpDown instructionLimit = new();
    private readonly NumericUpDown framebufferWidth = new();
    private readonly NumericUpDown framebufferHeight = new();
    private readonly Label stopValue = new();
    private readonly Label instructionValue = new();
    private readonly Label videoValue = new();
    private readonly Label serialValue = new();
    private readonly Label elapsedValue = new();
    private readonly FramebufferView framebufferView = new();
    private readonly TextBox summaryText = CreateLogBox();
    private readonly TextBox serialText = CreateLogBox();
    private readonly TextBox traceText = CreateLogBox();
    private readonly TextBox devicesText = CreateLogBox();

    private DreamcastRunResult? lastResult;

    public MainForm()
    {
        Text = "dcSharp";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1240;
        Height = 820;
        MinimumSize = new Size(960, 640);
        AllowDrop = true;

        ConfigureCommands();
        ConfigureInputs();
        BuildLayout();
        UpdateStatus("Ready");

        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
    }

    private void ConfigureCommands()
    {
        toolStrip.Dock = DockStyle.Top;
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.RenderMode = ToolStripRenderMode.System;
        toolStrip.Items.Add(runButton);
        toolStrip.Items.Add(runDemoButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(clearButton);

        runButton.Click += async (_, _) => await RunSelectedAsync();
        runDemoButton.Click += async (_, _) => await RunDemoAsync();
        clearButton.Click += (_, _) => ClearOutput();

        statusStrip.Dock = DockStyle.Bottom;
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(statusLabel);
    }

    private void ConfigureInputs()
    {
        instructionLimit.Minimum = 1;
        instructionLimit.Maximum = 500_000_000;
        instructionLimit.Increment = 1_000_000;
        instructionLimit.Value = DefaultInstructionLimit;
        instructionLimit.ThousandsSeparator = true;

        framebufferWidth.Minimum = 1;
        framebufferWidth.Maximum = 1024;
        framebufferWidth.Value = DefaultFramebufferWidth;

        framebufferHeight.Minimum = 1;
        framebufferHeight.Maximum = 768;
        framebufferHeight.Value = DefaultFramebufferHeight;

        elfPath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        mediaPath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browseElfButton.Anchor = AnchorStyles.Right;
        browseMediaButton.Anchor = AnchorStyles.Right;
        clearMediaButton.Anchor = AnchorStyles.Right;

        browseElfButton.Click += (_, _) => BrowseElf();
        browseMediaButton.Click += (_, _) => BrowseMedia();
        clearMediaButton.Click += (_, _) =>
        {
            mediaPath.Clear();
            UpdateStatus("Media cleared");
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 820
        };
        mainSplit.Panel1.Controls.Add(BuildPreviewPanel());
        mainSplit.Panel2.Controls.Add(BuildSidePanel());

        root.Controls.Add(mainSplit, 0, 0);
        root.Controls.Add(BuildDiagnosticsTabs(), 0, 1);

        Controls.Add(root);
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);
    }

    private Control BuildPreviewPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        framebufferView.Dock = DockStyle.Fill;
        panel.Controls.Add(framebufferView, 0, 0);
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0),
            Text = "Framebuffer preview uses the current RGB565 VRAM snapshot.",
            ForeColor = SystemColors.GrayText
        }, 0, 1);
        return panel;
    }

    private Control BuildSidePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 0, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(BuildRunGroup(), 0, 0);
        panel.Controls.Add(BuildFramebufferGroup(), 0, 1);
        panel.Controls.Add(BuildRunStatsGroup(), 0, 2);
        return panel;
    }

    private Control BuildRunGroup()
    {
        var grid = CreateSettingsGrid(7);
        grid.Controls.Add(CreateFieldLabel("ELF"), 0, 0);
        grid.Controls.Add(elfPath, 1, 0);
        grid.Controls.Add(browseElfButton, 2, 0);

        grid.Controls.Add(CreateFieldLabel("Media"), 0, 1);
        grid.Controls.Add(mediaPath, 1, 1);
        var mediaButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        mediaButtons.Controls.Add(browseMediaButton);
        mediaButtons.Controls.Add(clearMediaButton);
        grid.Controls.Add(mediaButtons, 2, 1);

        grid.Controls.Add(CreateFieldLabel("Instructions"), 0, 2);
        grid.Controls.Add(instructionLimit, 1, 2);
        grid.SetColumnSpan(instructionLimit, 2);

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttonRow.Controls.Add(new Button { Text = "Run Selected", AutoSize = true });
        buttonRow.Controls.Add(new Button { Text = "Run Demo", AutoSize = true });
        ((Button)buttonRow.Controls[0]).Click += async (_, _) => await RunSelectedAsync();
        ((Button)buttonRow.Controls[1]).Click += async (_, _) => await RunDemoAsync();
        grid.Controls.Add(buttonRow, 1, 3);
        grid.SetColumnSpan(buttonRow, 2);

        return WrapGroup("Run", grid);
    }

    private Control BuildFramebufferGroup()
    {
        var grid = CreateSettingsGrid(3);
        grid.Controls.Add(CreateFieldLabel("Width"), 0, 0);
        grid.Controls.Add(framebufferWidth, 1, 0);
        grid.SetColumnSpan(framebufferWidth, 2);
        grid.Controls.Add(CreateFieldLabel("Height"), 0, 1);
        grid.Controls.Add(framebufferHeight, 1, 1);
        grid.SetColumnSpan(framebufferHeight, 2);
        return WrapGroup("Framebuffer", grid);
    }

    private Control BuildRunStatsGroup()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddStat(grid, 0, "Stop", stopValue);
        AddStat(grid, 1, "Instructions", instructionValue);
        AddStat(grid, 2, "Video", videoValue);
        AddStat(grid, 3, "Serial", serialValue);
        AddStat(grid, 4, "Elapsed", elapsedValue);
        return WrapGroup("Last Run", grid);
    }

    private Control BuildDiagnosticsTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateTab("Summary", summaryText));
        tabs.TabPages.Add(CreateTab("Serial", serialText));
        tabs.TabPages.Add(CreateTab("Trace", traceText));
        tabs.TabPages.Add(CreateTab("Devices", devicesText));
        return tabs;
    }

    private async Task RunSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(elfPath.Text) || !File.Exists(elfPath.Text))
        {
            UpdateStatus("Select a valid ELF before running.");
            summaryText.Text = "No ELF selected.";
            return;
        }

        await RunAsync(elfPath.Text, mediaPath.Text, (ulong)instructionLimit.Value, "Running selected ELF...");
    }

    private async Task RunDemoAsync()
    {
        var demo = FindDemoElf();
        if (demo is null)
        {
            UpdateStatus("No demo ELF found under artifacts/kos. Build KOS fixtures first.");
            summaryText.Text = "No demo ELF found. Expected one of: dcsharp_framebuffer.elf, dcsharp_pvr_texture_rgb565.elf, dcsharp_probe.elf, dcsharp_minimal.elf.";
            return;
        }

        elfPath.Text = demo;
        mediaPath.Clear();
        instructionLimit.Value = DefaultInstructionLimit;
        await RunAsync(demo, string.Empty, DefaultInstructionLimit, $"Running demo {Path.GetFileName(demo)}...");
    }

    private async Task RunAsync(string elfFile, string mediaFile, ulong instructions, string status)
    {
        SetRunningState(true);
        ClearOutput(keepFramebuffer: true);
        UpdateStatus(status);

        var elapsed = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => ExecuteRun(elfFile, mediaFile, instructions));
            elapsed.Stop();
            lastResult = result;
            UpdateFramebuffer(result);
            UpdateDiagnostics(result, elapsed.Elapsed);
            UpdateStatus($"{Path.GetFileName(elfFile)} stopped as {result.StopReason} after {result.Cpu.InstructionsExecuted:N0} instructions.");
        }
        catch (Exception ex)
        {
            elapsed.Stop();
            summaryText.Text = ex.ToString();
            UpdateStatus("Run failed.");
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private static DreamcastRunResult ExecuteRun(string elfFile, string mediaFile, ulong instructions)
    {
        using var elfStream = File.OpenRead(elfFile);
        var elf = ElfFile.Read(elfStream);
        var media = string.IsNullOrWhiteSpace(mediaFile) ? null : DreamcastMediaImageLoader.LoadFromFile(mediaFile);
        return new DreamcastRunner().Run(elf, new DreamcastRunOptions(InstructionLimit: instructions, Media: media));
    }

    private void UpdateFramebuffer(DreamcastRunResult result)
    {
        var width = (int)framebufferWidth.Value;
        var height = (int)framebufferHeight.Value;
        framebufferView.SetFramebuffer(CreateBitmap(result.Video.Vram, width, height), width, height);
    }

    private static Bitmap CreateBitmap(ReadOnlySpan<byte> vram, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        var requiredBytes = width * height * 2;
        if (vram.Length < requiredBytes)
        {
            return bitmap;
        }

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 2;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 2;
                var pixel = (ushort)(vram[offset] | (vram[offset + 1] << 8));
                bitmap.SetPixel(x, y, Rgb565ToColor(pixel));
            }
        }

        return bitmap;
    }

    private static Color Rgb565ToColor(ushort pixel)
    {
        var rgba = DreamcastFramebufferPngWriter.Rgb565ToRgba32(pixel);
        return Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
    }

    private void UpdateDiagnostics(DreamcastRunResult result, TimeSpan elapsed)
    {
        var summary = DreamcastRunSummary.FromResult(result);
        stopValue.Text = result.StopReason.ToString();
        instructionValue.Text = result.Cpu.InstructionsExecuted.ToString("N0");
        videoValue.Text = $"{result.Video.NonZeroBytes:N0} nonzero";
        serialValue.Text = $"{result.SerialOutput.Count:N0} bytes";
        elapsedValue.Text = elapsed.ToString("hh\\:mm\\:ss\\.fff");

        summaryText.Text = BuildSummary(result, summary, elapsed);
        serialText.Text = result.SerialOutput.Count == 0
            ? "<no serial output>"
            : Encoding.ASCII.GetString(result.SerialOutput.ToArray());
        traceText.Text = BuildTrace(result);
        devicesText.Text = BuildDevices(result, summary);
    }

    private static string BuildSummary(DreamcastRunResult result, DreamcastRunSummary summary, TimeSpan elapsed)
    {
        var lines = new List<string>
        {
            $"Stop reason: {result.StopReason}",
            $"Detail: {result.StopDetail}",
            $"Elapsed: {elapsed:hh\\:mm\\:ss\\.fff}",
            $"Instructions: {result.Cpu.InstructionsExecuted:N0}",
            $"PC: 0x{result.Cpu.Pc:X8}",
            $"PR: 0x{result.Cpu.Pr:X8}",
            $"SR: 0x{result.Cpu.Sr:X8}",
            $"Loaded bytes: {result.Load.LoadedBytes:N0}",
            $"Segments: {result.Load.LoadedSegments.Count}",
            $"Symbols: {result.Load.Symbols.Count}",
            $"VRAM: {result.Video.NonZeroBytes:N0} nonzero bytes, checksum {result.Video.Fnv1A32Hex}",
            $"PVR: registers={summary.Video.PvrRegisterAccessCount:N0}, taWrites={summary.Video.PvrTaCommandWriteCount:N0}, strips={summary.Video.PvrTaStrips.Count:N0}",
            $"AICA: registers={summary.Audio.RegisterAccessCount:N0}, channels={summary.Audio.Channels.Count:N0}, active={summary.Audio.ActiveChannelCount:N0}",
            $"Maple: transfers={summary.Maple.TransferCount:N0}, dmaBatches={summary.Maple.DmaBatchCount:N0}",
            $"Scheduler: vblanks={summary.Scheduler.VBlankEventsRaised:N0}, hardwareTicks={summary.Scheduler.HardwareAdvanceTicks:N0}, fastForward={summary.Scheduler.CpuFastForwardInstructions:N0}"
        };

        if (result.StopPc is { } stopPc)
        {
            lines.Add($"Stop PC: 0x{stopPc:X8}");
        }

        if (summary.StopSymbol is { } symbol)
        {
            lines.Add($"Stop symbol: {symbol.Name}+0x{symbol.Offset:X}");
        }

        if (summary.Video.PvrTaStrips.Count > 0)
        {
            lines.Add("");
            lines.Add("PVR strips:");
            lines.AddRange(summary.Video.PvrTaStrips.TakeLast(8).Select(strip =>
                $"  {strip.ListTypeName ?? "none"} vertices={strip.VertexCount} color={strip.Rgb565Hex} mode2={strip.HeaderPayload?.Mode2Hex ?? "none"} mode3={strip.HeaderPayload?.Mode3Hex ?? "none"}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTrace(DreamcastRunResult result)
    {
        if (result.TraceTail.Count == 0)
        {
            return "<trace tail disabled>";
        }

        return string.Join(Environment.NewLine, result.TraceTail.Select(step =>
            $"0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}"));
    }

    private static string BuildDevices(DreamcastRunResult result, DreamcastRunSummary summary)
    {
        var lines = new List<string>();
        lines.Add("Domains:");
        lines.AddRange(summary.DeviceAccessDomains.Select(domain => $"  {domain.Domain}: {domain.Count:N0}"));
        lines.Add("");
        lines.Add("Kinds:");
        lines.AddRange(summary.DeviceAccessKinds.Select(kind => $"  {kind.Kind}: {kind.Count:N0}"));
        lines.Add("");
        lines.Add("Recent accesses:");
        lines.AddRange(result.DeviceAccesses.TakeLast(64).Select(access =>
            $"  {DreamcastDeviceDomainClassifier.Classify(access),-8} {access.Kind,-13} addr=0x{access.Address:X8} size={access.Size} value=0x{access.Value:X8}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void BrowseElf()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Dreamcast ELF",
            Filter = "Dreamcast ELF (*.elf)|*.elf|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            elfPath.Text = dialog.FileName;
            UpdateStatus($"Selected ELF: {Path.GetFileName(dialog.FileName)}");
        }
    }

    private void BrowseMedia()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select optional media image",
            Filter = "Disc images (*.cue;*.bin;*.raw)|*.cue;*.bin;*.raw|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            mediaPath.Text = dialog.FileName;
            UpdateStatus($"Selected media: {Path.GetFileName(dialog.FileName)}");
        }
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (string.Equals(extension, ".elf", StringComparison.OrdinalIgnoreCase))
            {
                elfPath.Text = file;
            }
            else if (string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".raw", StringComparison.OrdinalIgnoreCase))
            {
                mediaPath.Text = file;
            }
        }

        UpdateStatus("Loaded dropped file path.");
    }

    private static string? FindDemoElf()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return null;
        }

        var artifactDirectory = Path.Combine(repoRoot, "artifacts", "kos");
        string[] candidates =
        [
            "dcsharp_framebuffer.elf",
            "dcsharp_pvr_texture_rgb565.elf",
            "dcsharp_probe.elf",
            "dcsharp_minimal.elf"
        ];
        return candidates
            .Select(candidate => Path.Combine(artifactDirectory, candidate))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dcSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void SetRunningState(bool running)
    {
        runButton.Enabled = !running;
        runDemoButton.Enabled = !running;
        browseElfButton.Enabled = !running;
        browseMediaButton.Enabled = !running;
        clearMediaButton.Enabled = !running;
        instructionLimit.Enabled = !running;
        framebufferWidth.Enabled = !running;
        framebufferHeight.Enabled = !running;
        Cursor = running ? Cursors.WaitCursor : Cursors.Default;
    }

    private void ClearOutput(bool keepFramebuffer = false)
    {
        summaryText.Clear();
        serialText.Clear();
        traceText.Clear();
        devicesText.Clear();
        stopValue.Text = "";
        instructionValue.Text = "";
        videoValue.Text = "";
        serialValue.Text = "";
        elapsedValue.Text = "";
        lastResult = null;
        if (!keepFramebuffer)
        {
            framebufferView.Clear();
        }

        UpdateStatus("Cleared");
    }

    private void UpdateStatus(string text)
    {
        statusLabel.Text = text;
    }

    private static Label CreateFieldLabel(string text) =>
        new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };

    private static TextBox CreateLogBox() =>
        new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 10)
        };

    private static TableLayoutPanel CreateSettingsGrid(int rows)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = rows,
            Padding = new Padding(8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static GroupBox WrapGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            Text = title
        };
        group.Controls.Add(content);
        return group;
    }

    private static TabPage CreateTab(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private static void AddStat(TableLayoutPanel grid, int row, string name, Label value)
    {
        value.AutoEllipsis = true;
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        grid.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText }, 0, row);
        grid.Controls.Add(value, 1, row);
    }
}

internal sealed class FramebufferView : Control
{
    private Bitmap? framebuffer;
    private int framebufferWidth;
    private int framebufferHeight;

    public FramebufferView()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
    }

    public void SetFramebuffer(Bitmap bitmap, int width, int height)
    {
        framebuffer?.Dispose();
        framebuffer = bitmap;
        framebufferWidth = width;
        framebufferHeight = height;
        Invalidate();
    }

    public void Clear()
    {
        framebuffer?.Dispose();
        framebuffer = null;
        framebufferWidth = 0;
        framebufferHeight = 0;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            framebuffer?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.Black);

        if (framebuffer is null)
        {
            DrawEmptyState(e.Graphics);
        }
        else
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(framebuffer, DestinationRectangle(framebuffer.Width, framebuffer.Height));
            using var brush = new SolidBrush(Color.FromArgb(210, Color.White));
            e.Graphics.DrawString($"{framebufferWidth}x{framebufferHeight} RGB565", Font, brush, 10, Height - 26);
        }

        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlDark, ButtonBorderStyle.Solid);
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
        var text = "Run an ELF to show the RGB565 framebuffer";
        var size = graphics.MeasureString(text, Font);
        graphics.DrawString(text, Font, brush, (Width - size.Width) / 2, (Height - size.Height) / 2);
    }

    private Rectangle DestinationRectangle(int imageWidth, int imageHeight)
    {
        var scale = Math.Min(Width / (float)imageWidth, Height / (float)imageHeight);
        var width = Math.Max(1, (int)(imageWidth * scale));
        var height = Math.Max(1, (int)(imageHeight * scale));
        return new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
    }
}
