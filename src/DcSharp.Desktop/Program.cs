using DcSharp.Core.Media;
using DcSharp.Core.Execution;
using DcSharp.Core.Loading;
using System.Diagnostics;
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
    private readonly TextBox elfPath = new();
    private readonly TextBox mediaPath = new();
    private readonly NumericUpDown instructionLimit = new();
    private readonly Button runButton = new();
    private readonly Button runWithDefaultsButton = new();
    private readonly TextBox output = new();
    private readonly Button browseElfButton = new();
    private readonly Button browseMediaButton = new();
    private readonly Button clearButton = new();

    public MainForm()
    {
        Text = "DcSharp Desktop";
        Width = 900;
        Height = 700;
        MinimumSize = new Size(760, 560);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pathsPanel = CreatePathsPanel();
        var controlsPanel = CreateControlsPanel();
        output.Multiline = true;
        output.ScrollBars = ScrollBars.Both;
        output.ReadOnly = true;
        output.WordWrap = false;
        output.Font = new Font(FontFamily.GenericMonospace, 10);

        layout.Controls.Add(pathsPanel);
        layout.Controls.Add(controlsPanel);
        layout.Controls.Add(output);
        layout.SetRow(output, 2);

        Controls.Add(layout);
    }

    private Control CreatePathsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var mediaLabel = new Label { Text = "Media (.cue/.bin):", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var elfLabel = new Label { Text = "ELF:", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        elfPath.Dock = DockStyle.Fill;
        mediaPath.Dock = DockStyle.Fill;
        browseElfButton.Text = "Browse…";
        browseMediaButton.Text = "Browse…";
        browseElfButton.AutoSize = true;
        browseMediaButton.AutoSize = true;
        browseElfButton.Click += (_, _) => BrowseElf();
        browseMediaButton.Click += (_, _) => BrowseMedia();

        panel.Controls.Add(elfLabel, 0, 0);
        panel.Controls.Add(elfPath, 1, 0);
        panel.Controls.Add(browseElfButton, 2, 0);
        panel.Controls.Add(mediaLabel, 0, 1);
        panel.Controls.Add(mediaPath, 1, 1);
        panel.Controls.Add(browseMediaButton, 2, 1);
        panel.SetColumnSpan(mediaPath, 1);
        panel.RowCount = 2;

        return panel;
    }

    private Control CreateControlsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            AutoSize = true
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        instructionLimit.Minimum = 1;
        instructionLimit.Maximum = 1_000_000;
        instructionLimit.Value = 8_192;
        instructionLimit.Width = 120;

        runButton.Text = "Run";
        runWithDefaultsButton.Text = "Run Demo (8 instructions)";
        clearButton.Text = "Clear";

        runButton.Click += async (_, _) => await RunAsync();
        runWithDefaultsButton.Click += async (_, _) => await RunAsync(8, mediaPath.Text);
        clearButton.Click += (_, _) => output.Clear();

        panel.Controls.Add(new Label { Text = "Instruction limit:", AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(instructionLimit);
        panel.Controls.Add(runButton);
        panel.Controls.Add(runWithDefaultsButton);
        panel.Controls.Add(clearButton);

        return panel;
    }

    private async Task RunAsync()
    {
        await RunAsync((int)instructionLimit.Value, mediaPath.Text);
    }

    private async Task RunAsync(int instructionCount, string mediaFile)
    {
        if (string.IsNullOrWhiteSpace(elfPath.Text) || !File.Exists(elfPath.Text))
        {
            ShowStatus("Please select a valid ELF file.");
            return;
        }

        runButton.Enabled = false;
        runWithDefaultsButton.Enabled = false;
        output.Clear();
        output.Text = "Running...\r\n";

        var elapsed = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(() => ExecuteRun(elfPath.Text, instructionCount, mediaFile));
            ShowStatus(BuildResultSummary(result, elapsed.Elapsed));
        }
        catch (Exception ex)
        {
            ShowStatus(ex.ToString());
        }
        finally
        {
            runButton.Enabled = true;
            runWithDefaultsButton.Enabled = true;
        }
    }

    private static DreamcastRunResult ExecuteRun(string elfFile, int instructionCount, string mediaFile)
    {
        using var elfStream = File.OpenRead(elfFile);
        var elf = ElfFile.Read(elfStream);
        var media = string.IsNullOrWhiteSpace(mediaFile) ? null : DreamcastMediaImageLoader.LoadFromFile(mediaFile);
        var options = new DreamcastRunOptions(InstructionLimit: (ulong)instructionCount, Media: media);
        return new DreamcastRunner().Run(elf, options);
    }

    private static string BuildResultSummary(DreamcastRunResult result, TimeSpan elapsed)
    {
        var lines = new List<string>
        {
            $"Stopped: {result.StopReason}",
            $"Time: {elapsed:hh\\:mm\\:ss\\.fff}",
            $"Instructions: {result.Cpu.InstructionsExecuted}",
            $"PC: 0x{result.Cpu.Pc:X8}",
            $"PR: 0x{result.Cpu.Pr:X8}",
            $"SR: 0x{result.Cpu.Sr:X8}",
            $"Video nonzero bytes: {result.Video.NonZeroBytes}",
            $"Video checksum: {result.Video.Fnv1A32Hex}",
            $"Serial bytes: {result.SerialOutput.Count}",
            $"Device accesses: {result.DeviceAccesses.Count}"
        };

        if (result.StopPc is { } stopPc)
        {
            lines.Add($"Stop PC: 0x{stopPc:X8}");
        }

        if (result.StopDetail is not null)
        {
            lines.Add($"Detail: {result.StopDetail}");
        }

        if (result.SerialOutput.Count > 0)
        {
            lines.Add($"Serial: {Encoding.ASCII.GetString(result.SerialOutput.ToArray())}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void BrowseElf()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Dreamcast ELF",
            Filter = "Executable (*.elf)|*.elf|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            elfPath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(mediaPath.Text))
            {
                var mediaGuess = Path.Combine(Path.GetDirectoryName(dialog.FileName) ?? string.Empty, Path.GetFileNameWithoutExtension(dialog.FileName) + ".cue");
                if (File.Exists(mediaGuess))
                {
                    mediaPath.Text = mediaGuess;
                }
            }
        }
    }

    private void BrowseMedia()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select disc image (optional)",
            Filter = "Disc image (*.cue;*.bin)|*.cue;*.bin|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            mediaPath.Text = dialog.FileName;
        }
    }

    private void ShowStatus(string text)
    {
        output.Text = text;
        output.SelectionStart = output.TextLength;
        output.ScrollToCaret();
    }
}
