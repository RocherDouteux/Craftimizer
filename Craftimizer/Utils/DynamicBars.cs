using Craftimizer.Plugin;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Linq;
using Dalamud.Utility.Numerics;
using Dalamud.Interface;

namespace Craftimizer.Utils;

internal static class DynamicBars
{
    public readonly record struct BarData(string Name, Vector4 Color, SimulatedMacro.Reliablity.Param? Reliability, float Value, float Max, IReadOnlyList<int?>? Collectability = null, string? Caption = null, string? DefaultCaptionSizeText = null, Action<DrawerParams>? CustomDrawer = null)
    {
        public BarData(string name, Action<DrawerParams> customDrawer) : this(name, default, null, 0, 0, null, null, null, customDrawer)
        {

        }

        public BarData(string name, Vector4 color, float value, float max) : this(name, color, null, value, max, null, null, null)
        {

        }
    }

    public readonly record struct DrawerParams(float TotalSize, float Spacing);

    public static float GetTextSize(IEnumerable<BarData> bars) =>
        bars.Max(b =>
        {
            if (b.CustomDrawer is { })
                return 0;
            var defaultSize = 0f;
            if (b.DefaultCaptionSizeText is { } defaultCaptionSizeText)
                defaultSize = ImGui.CalcTextSize(defaultCaptionSizeText).X;
            if (b.Caption is { } caption)
                return Math.Max(ImGui.CalcTextSize(caption).X, defaultSize);
            // max (sp/2) "/" (sp/2) max
            return Math.Max(
                Math.Max(ImGui.CalcTextSize($"{b.Value:0}").X, ImGui.CalcTextSize($"{b.Max:0}").X) * 2
                + ImGui.GetStyle().ItemSpacing.X
                + ImGui.CalcTextSize("/").X,
                defaultSize);
        });

    private static ImRaii.Color? PushCollectableColor(this in BarData bar, float collectability, bool colorUnmetThreshold = true)
    {
        if (bar.Collectability is not { } collectabilities)
            return null;

        var ret = collectabilities.Count;
        for (var i = 0; i < collectabilities.Count; ++i)
        {
            if (collectability < collectabilities[i])
            {
                ret = i;
                break;
            }
        }

        if (ret == 0)
        {
            if (colorUnmetThreshold)
                return ImRaii.PushColor(ImGuiCol.Text, Colors.Collectability);
            return null;
        }

        return ImRaii.PushColor(ImGuiCol.Text, Colors.CollectabilityThreshold[ret - 1]);
    }

    public static void Draw(IEnumerable<BarData> bars, float? textSize = null)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalSize = ImGui.GetContentRegionAvail().X;
        totalSize -= 2 * spacing;
        textSize ??= GetTextSize(bars);
        var maxSize = (textSize.Value - 2 * spacing - ImGui.CalcTextSize("/").X) / 2;
        var barSize = totalSize - textSize.Value - spacing;
        foreach (var bar in bars)
        {
            using var panel = ImRaii2.GroupPanel(bar.Name, totalSize, out _);
            if (bar.CustomDrawer is { } drawer)
                drawer(new(totalSize, spacing));
            else
            {
                var pos = ImGui.GetCursorPos();
                var screenPos = ImGui.GetCursorScreenPos();
                using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, bar.Color))
                    ImGuiUtils.ProgressBar(Math.Clamp(bar.Value / bar.Max, 0, 1), new(barSize, ImGui.GetFrameHeight()));
                if (bar.Collectability is { } collectability)
                {
                    var i = 0;
                    var rounding = ImGui.GetStyle().FrameRounding;
                    var height = ImGui.GetFrameHeight();
                    foreach (var (c, color) in collectability.Zip(Colors.CollectabilityThreshold))
                    {
                        ++i;
                        if (c is not { } threshold)
                            continue;
                        var offset = barSize * threshold / bar.Max;
                        var isLast = i == collectability.Count;
                        var offsetNext = isLast ? barSize : barSize * collectability[i]!.Value / bar.Max;
                        var passedThreshold = bar.Value >= threshold;
                        ImGui.GetWindowDrawList().AddRectFilled(
                            screenPos + new Vector2(offset, 0),
                            screenPos + new Vector2(offsetNext, height),
                            ImGui.GetColorU32(color.WithW(passedThreshold ? 0.6f : 0.2f)),
                            isLast ? rounding : 0
                        );
                        ImGui.GetWindowDrawList().AddLine(
                            screenPos + new Vector2(offset, 0),
                            screenPos + new Vector2(offset, height),
                            ImGui.GetColorU32(color),
                            Math.Max(passedThreshold ? 3 : 1.5f, rounding / 2f)
                        );
                    }
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenOverlapped))
                {
                    if (bar.Reliability is { } reliability)
                    {
                        if (reliability.GetViolinData(bar.Max, (int)(barSize / 5), 0.02) is { } violinData)
                        {
                            ImGui.SetCursorPos(pos);
                            ImGuiUtils.ViolinPlot(violinData, new(barSize, ImGui.GetFrameHeight()));
                            if (ImGui.IsItemHovered())
                            {
                                using var _font = ImRaii.PushFont(UiBuilder.DefaultFont);
                                using var _tooltip = ImRaii.Tooltip();
                                using var _ = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

                                ImGui.TextUnformatted("Min: ");
                                ImGui.SameLine(0, 0);
                                using (var color = bar.PushCollectableColor(reliability.Min))
                                    ImGui.TextUnformatted(reliability.Min.ToString());

                                ImGui.TextUnformatted("Med: ");
                                ImGui.SameLine(0, 0);
                                using (var color = bar.PushCollectableColor(reliability.Median))
                                    ImGui.TextUnformatted(reliability.Median.ToString());

                                ImGui.TextUnformatted("Avg: ");
                                ImGui.SameLine(0, 0);
                                using (var color = bar.PushCollectableColor(reliability.Average))
                                    ImGui.TextUnformatted(reliability.Average.ToString());

                                ImGui.TextUnformatted("Max: ");
                                ImGui.SameLine(0, 0);
                                using (var color = bar.PushCollectableColor(reliability.Max))
                                    ImGui.TextUnformatted(reliability.Max.ToString());
                            }
                        }
                    }
                }
                ImGui.SameLine(0, spacing);
                ImGui.AlignTextToFramePadding();
                using var _color = bar.PushCollectableColor(bar.Value, false);
                if (bar.Caption is { } caption)
                    ImGuiUtils.TextRight(caption, textSize.Value);
                else
                {
                    ImGuiUtils.TextRight($"{bar.Value:0}", maxSize);
                    ImGui.SameLine(0, spacing / 2);
                    ImGui.TextUnformatted("/");
                    ImGui.SameLine(0, spacing / 2);
                    ImGuiUtils.TextRight($"{bar.Max:0}", maxSize);
                }
            }
        }
    }

    public static void DrawProgressBar(Solver.Solver solver, float? availSpace = null)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        availSpace ??= ImGui.GetContentRegionAvail().X;

        var fraction = (float)solver.ProgressValue / solver.ProgressMax;
        if (Service.Configuration.ProgressType == Configuration.ProgressBarType.None)
        {
            ImGui.AlignTextToFramePadding();
            ImGuiUtils.TextCentered($"{fraction * 100:N0}%", availSpace.Value);

            if (ImGui.IsItemHovered())
                DrawProgressBarTooltip(solver);
            return;
        }

        var percentWidth = ImGui.CalcTextSize("100%").X;
        var progressWidth = availSpace.Value;
        var progressColors = Colors.GetSolverProgressColors(solver.ProgressStage);

        fraction = Math.Clamp(fraction, 0, 1);

        if (!solver.IsIndeterminate)
            progressWidth -= percentWidth + spacing;
        else
            fraction = (float)-ImGui.GetTime() * .5f;

        using (ImRaii.PushColor(ImGuiCol.FrameBg, progressColors.Background))
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, progressColors.Foreground))
            ImGuiUtils.ProgressBar(fraction, new(progressWidth, ImGui.GetFrameHeight()));
        if (ImGui.IsItemHovered())
            DrawProgressBarTooltip(solver);

        if (!solver.IsIndeterminate)
        {
            ImGui.SameLine(0, spacing);
            ImGui.AlignTextToFramePadding();
            ImGuiUtils.TextRight($"{fraction * 100:N0}%", percentWidth);
        }
    }

    public static void DrawProgressBarTooltip(Solver.Solver solver)
    {
        string tooltip;
        if (solver.IsIndeterminate)
            tooltip = "Initializing";
        else
        {
            tooltip = $"Solver Progress: {solver.ProgressValue:N0} / {solver.ProgressMax:N0}";
            if (solver.ProgressValue > solver.ProgressMax)
                tooltip += $"\n\nThis is taking longer than expected. Check to see if your gear stats are good and the solver settings are adequate.";
        }
        ImGuiUtils.TooltipWrapped(tooltip);
    }
}
