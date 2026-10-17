
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class SpeedrunVisualizer : MonoBehaviour
{
    [Serializable]
    public class Split
    {
        public string name;
        public float time;
    }

    [Serializable]
    public class Run
    {
        public float time;
        public string bastionType;
        public string overworldType;
        public Split[] splits;
    }

    [Serializable]
    private class RunFile
    {
        public Run[] runs;
    }

    private enum ViewMode { Runs, Distribution }

    [Header("Animation")]
    [SerializeField] private float secondsPerRun = 0.1f;
    [SerializeField] private float barLerpSpeed = 8f;
    [SerializeField] private int histogramBins = 12;

    private readonly List<Run> allRuns = new();
    private readonly List<Run> visibleRuns = new();
    private readonly List<Image> bars = new();
    private readonly List<float> targetHeights = new();
    private readonly List<Color> groupColors = new();
    private readonly List<string> groupNames = new();
    private readonly List<int> groupCounts = new();
    private readonly List<Text> groupLabels = new();
    private readonly List<float> groupAverageTimes = new();

    // Layout information for grouping runs.
    private readonly List<int> groupEndIndices = new();
    private readonly List<float> barXPositions = new();
    private float barSlotWidth;

    private Canvas canvas;
    private RectTransform chart;
    private RectTransform minuteGridRoot;
    private Text statusText;
    private Dropdown sortDropdown;
    private Dropdown bastionDropdown;
    private Dropdown overworldDropdown;
    private Button playButton;
    private Coroutine animationRoutine;

    private bool isAnimating;
    private ViewMode viewMode = ViewMode.Runs;
    private float maxTime = 1f;

    private void Start()
    {
        BuildUI();
        LoadRuns();
        PopulateFilters();
        ApplyFiltersAndSort();
    }

    private void Update()
    {
        for (int i = 0; i < bars.Count; i++)
        {
            if (bars[i] == null) continue;

            RectTransform rect = bars[i].rectTransform;
            Vector2 size = rect.sizeDelta;
            size.y = Mathf.Lerp(size.y, targetHeights[i],
                Time.deltaTime * barLerpSpeed);
            rect.sizeDelta = size;
        }
    }

    private void LoadRuns()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath, "runs.json");

        if (!File.Exists(path))
        {
            SetStatus("ERROR: runs.json not found in StreamingAssets.");
            Debug.LogError("Could not find runs.json at: " + path);
            return;
        }

        try
        {
            string json = File.ReadAllText(path);

            // JsonUtility requires an object around a top-level array.
            RunFile file = JsonUtility.FromJson<RunFile>(
                "{\"runs\":" + json + "}");

            if (file == null || file.runs == null)
            {
                SetStatus("ERROR: JSON did not contain a run array.");
                return;
            }

            allRuns.Clear();
            allRuns.AddRange(file.runs.Where(r => r != null && r.time > 0 && r.time > 535));

            SetStatus($"Loaded {allRuns.Count} runs.");
        }
        catch (Exception ex)
        {
            SetStatus("ERROR reading JSON. Check the Console.");
            Debug.LogException(ex);
        }
    }


    private void ApplyFiltersAndSort()
    {
        if (sortDropdown == null ||
            bastionDropdown == null ||
            overworldDropdown == null)
            return;

        string bastion = bastionDropdown.options[
            bastionDropdown.value].text;

        string overworld = overworldDropdown.options[
            overworldDropdown.value].text;

        bool groupByBastion = bastion != "All";
        bool groupByOverworld = overworld != "All";

        // Grouping controls organize runs; they no longer filter runs out.
        IEnumerable<IGrouping<string, Run>> groups;

        if (groupByBastion && groupByOverworld)
        {
            groups = allRuns
                .GroupBy(r =>
                    $"{SafeType(r.bastionType)} / {SafeType(r.overworldType)}")
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }
        else if (groupByBastion)
        {
            groups = allRuns
                .GroupBy(r => SafeType(r.bastionType))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }
        else if (groupByOverworld)
        {
            groups = allRuns
                .GroupBy(r => SafeType(r.overworldType))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // "All" means one continuous group.
            groups = new[]
            {
            allRuns.GroupBy(_ => "All").FirstOrDefault()
        }.Where(g => g != null);
        }

        visibleRuns.Clear();
        groupEndIndices.Clear();
        groupColors.Clear();
        groupNames.Clear();
        groupCounts.Clear();
        groupLabels.Clear();
        groupAverageTimes.Clear();

        foreach (var group in groups)
        {
            groupColors.Add(GetGroupColor(groupColors.Count));

            IEnumerable<Run> sortedGroup = group;

            if (sortDropdown.value == 0)
                sortedGroup = sortedGroup.OrderBy(r => r.time);
            else if (sortDropdown.value == 1)
                sortedGroup = sortedGroup.OrderByDescending(r => r.time);

            visibleRuns.AddRange(sortedGroup);
            groupEndIndices.Add(visibleRuns.Count);
            groupNames.Add(group.Key);
            groupCounts.Add(group.Count());
            groupAverageTimes.Add(
                 group.Any() ? group.Average(r => r.time) : 0f
            );
        }

        maxTime = Mathf.Max(
            1f,
            visibleRuns.Count > 0 ? visibleRuns.Max(r => r.time) : 1f);

        viewMode = ViewMode.Runs;
        StopAnimation();
        ClearBars();
        StartAnimation();
    }

    private void CreateMinuteGrid()
    {
        if (minuteGridRoot != null)
        {
            Destroy(minuteGridRoot.gameObject);
        }

        GameObject gridObject = new GameObject(
            "MinuteGrid",
            typeof(RectTransform)
        );

        minuteGridRoot = gridObject.GetComponent<RectTransform>();
        minuteGridRoot.SetParent(chart, false);
        minuteGridRoot.anchorMin = Vector2.zero;
        minuteGridRoot.anchorMax = Vector2.one;
        minuteGridRoot.offsetMin = Vector2.zero;
        minuteGridRoot.offsetMax = Vector2.zero;
        minuteGridRoot.SetAsFirstSibling();

        // Match the height reserved for the bars.
        float availableHeight = Mathf.Max(2f, chart.rect.height - 45f);

        // Add a line for every whole minute up to the longest run.
        int maxMinute = Mathf.FloorToInt(maxTime / 60f);

        for (int minute = 1; minute <= maxMinute; minute++)
        {
            float y = (minute * 60f / maxTime) * availableHeight;

            // Horizontal grid line
            GameObject lineObject = new GameObject(
                $"MinuteLine_{minute}",
                typeof(RectTransform),
                typeof(Image)
            );

            RectTransform lineRect =
                lineObject.GetComponent<RectTransform>();

            lineRect.SetParent(minuteGridRoot, false);
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(1f, 0f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.anchoredPosition = new Vector2(0f, y);
            lineRect.sizeDelta = new Vector2(0f, 1f);

            Image lineImage = lineObject.GetComponent<Image>();
            lineImage.color = new Color(1f, 1f, 1f, 0.18f);
            lineImage.raycastTarget = false;

            // Minute label, positioned just left of the chart.
            GameObject labelObject = new GameObject(
                $"MinuteLabel_{minute}",
                typeof(RectTransform),
                typeof(Text)
            );

            RectTransform labelRect =
                labelObject.GetComponent<RectTransform>();

            labelRect.SetParent(minuteGridRoot, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 0f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            labelRect.anchoredPosition = new Vector2(-8f, y);
            labelRect.sizeDelta = new Vector2(45f, 20f);

            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf"
            );
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleRight;
            label.color = new Color(1f, 1f, 1f, 0.75f);
            label.text = $"{minute}:00";
            label.raycastTarget = false;
        }
    }

    private void CreateGroupLabels()
    {
        // Remove any labels from a previous animation.
        foreach (Text label in groupLabels)
        {
            if (label != null)
                Destroy(label.gameObject);
        }

        groupLabels.Clear();

        if (groupNames.Count == 0 || barXPositions.Count == 0)
            return;

        int startIndex = 0;

        for (int g = 0; g < groupNames.Count; g++)
        {
            int endIndex = groupEndIndices[g];
            if (endIndex <= startIndex)
                continue;

            float firstX = barXPositions[startIndex];
            float lastX = barXPositions[endIndex - 1];
            float centerX = (firstX + lastX) * 0.5f;

            GameObject labelObject = new GameObject(
                "Group Label " + groupNames[g],
                typeof(RectTransform),
                typeof(Text));

            labelObject.transform.SetParent(chart, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(
                Mathf.Max(90f, lastX - firstX + barSlotWidth),
                42f);

            rect.anchoredPosition = new Vector2(centerX, -4f);

            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            label.fontSize = 14;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.UpperCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.color = groupColors[g];
            float averageSeconds = groupAverageTimes[g];
            int totalSeconds = Mathf.RoundToInt(averageSeconds);

            string averageFormatted = string.Format(
                "{0}:{1:00}",
                totalSeconds / 60,
                totalSeconds % 60
            );

            label.text =
                $"{groupNames[g]}\n" +
                $"{groupCounts[g]} runs · Avg {averageFormatted}";

            groupLabels.Add(label);

            startIndex = endIndex;
        }
    }

    private Color GetGroupColor(int groupIndex)
    {
        Color[] palette =
        {
        new Color(0.20f, 0.65f, 1.00f), // Blue
        new Color(1.00f, 0.40f, 0.35f), // Red
        new Color(0.30f, 0.85f, 0.55f), // Green
        new Color(1.00f, 0.70f, 0.20f), // Orange
        new Color(0.70f, 0.45f, 1.00f), // Purple
        new Color(1.00f, 0.40f, 0.75f), // Pink
        new Color(0.20f, 0.85f, 0.85f), // Cyan
        new Color(0.85f, 0.85f, 0.35f)  // Yellow
    };

        return palette[groupIndex % palette.Length];
    }

    private Color GetColorForRunIndex(int index)
    {
        for (int groupIndex = 0;
             groupIndex < groupEndIndices.Count;
             groupIndex++)
        {
            int groupStart = groupIndex == 0
                ? 0
                : groupEndIndices[groupIndex - 1];

            if (index >= groupStart &&
                index < groupEndIndices[groupIndex])
            {
                return groupColors[groupIndex];
            }
        }

        return Color.white;
    }

    private string SafeType(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }


    private void StartAnimation()
    {
        StopAnimation();
        ClearBars();

        if (viewMode == ViewMode.Distribution)
        {
            BuildHistogram();
            return;
        }

        if (visibleRuns.Count == 0)
        {
            SetStatus("No runs match these filters.");
            return;
        }

        PrepareBarLayout();
        CreateMinuteGrid();
        CreateGroupLabels();
        animationRoutine = StartCoroutine(AnimateRuns());
    }


    private void PrepareBarLayout()
    {
        barXPositions.Clear();

        if (visibleRuns.Count == 0)
        {
            barSlotWidth = 0f;
            return;
        }

        // One and a half empty bar slots between groups.
        const float gapSlots = 1.5f;

        int groupCount = Mathf.Max(1, groupEndIndices.Count);

        float totalSlots =
            visibleRuns.Count + gapSlots * (groupCount - 1);

        barSlotWidth = chart.rect.width / totalSlots;

        int runIndex = 0;
        float slotCursor = 0f;

        foreach (int groupEnd in groupEndIndices)
        {
            while (runIndex < groupEnd)
            {
                barXPositions.Add(
                    (slotCursor + 0.5f) * barSlotWidth);

                slotCursor += 1f;
                runIndex++;
            }

            // Leave a visible gap before the next group.
            slotCursor += gapSlots;
        }
    }


    private IEnumerator AnimateRuns()
    {
        isAnimating = true;
        playButton.GetComponentInChildren<Text>().text = "Restart";

        for (int i = 0; i < visibleRuns.Count; i++)
        {
            AddBar(visibleRuns[i].time, maxTime, i);
            SetStatus($"Showing {i + 1} / {visibleRuns.Count} runs");

            // Add a run every 0.1 seconds (configurable in Inspector).
            yield return new WaitForSeconds(secondsPerRun);
        }

        isAnimating = false;
        SetStatus($"Complete — {visibleRuns.Count} runs displayed.");
        playButton.GetComponentInChildren<Text>().text = "Replay";
    }

    private void StopAnimation()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }

        isAnimating = false;
    }

    private void ClearBars()
    {
        foreach (Transform child in chart)
            Destroy(child.gameObject);

        bars.Clear();
        targetHeights.Clear();
    }

    private void AddBar(float value, float scaleMax, int index)
    {
        GameObject go = new GameObject("Bar " + index,
            typeof(RectTransform), typeof(Image));
        go.transform.SetParent(chart, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);

        float width = Mathf.Max(2f, barSlotWidth * 0.72f);

        rect.sizeDelta = new Vector2(width, 0f);
        rect.anchoredPosition = new Vector2(barXPositions[index], 0f);

        Image image = go.GetComponent<Image>();
        image.color = Color.Lerp(
            new Color(0.22f, 0.75f, 1f),
            new Color(0.65f, 0.35f, 1f),
            Mathf.Clamp01(value / scaleMax));

        image.color = GetColorForRunIndex(index);

        // Initialize the gradient component.
        VerticalGradient gradient = go.GetComponent<VerticalGradient>();

        if (gradient == null)
        {
            gradient = go.AddComponent<VerticalGradient>();
        }

        Color baseColor = GetColorForRunIndex(index);

        gradient.SetColors(
            Color.Lerp(baseColor, Color.white, 0.45f), // Top: lighter
            Color.Lerp(baseColor, Color.black, 0.25f)  // Bottom: darker
        );

        bars.Add(image);

        float availableHeight = Mathf.Max(2f, chart.rect.height - 45f);

        targetHeights.Add(Mathf.Max(
            2f,
            value / scaleMax * availableHeight));
    }

    private void BuildHistogram()
    {
        ClearBars();

        if (visibleRuns.Count == 0)
        {
            SetStatus("No runs to display.");
            return;
        }

        int bins = Mathf.Clamp(histogramBins, 1, 40);
        float min = visibleRuns.Min(r => r.time);
        float max = visibleRuns.Max(r => r.time);
        float range = Mathf.Max(0.001f, max - min);
        int[] counts = new int[bins];

        foreach (Run run in visibleRuns)
        {
            int bin = Mathf.Min(bins - 1,
                Mathf.FloorToInt((run.time - min) / range * bins));
            counts[bin]++;
        }

        int tallest = Mathf.Max(1, counts.Max());

        // Histogram bars use counts for height, not completion time.
        for (int i = 0; i < bins; i++)
        {
            float normalizedHeight = (float)counts[i] / tallest;
            AddHistogramBar(i, bins, normalizedHeight);
        }

        SetStatus($"Distribution of {visibleRuns.Count} runs — " +
            $"{min:F1}s to {max:F1}s");
    }

    private void AddHistogramBar(int index, int count, float normalizedHeight)
    {
        GameObject go = new GameObject("Bin " + index,
            typeof(RectTransform), typeof(Image));
        go.transform.SetParent(chart, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0f);

        float slotWidth = chart.rect.width / count;
        rect.sizeDelta = new Vector2(slotWidth * 0.72f, 0f);
        rect.anchoredPosition = new Vector2(
            (index + 0.5f) * slotWidth, 0f);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.35f, 0.75f, 1f);

        bars.Add(image);
        targetHeights.Add(Mathf.Max(2f,
            normalizedHeight * chart.rect.height));
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("Visualizer Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 0.5f;

        Image background = CreateImage(
            canvas.transform, "Background",
            new Color(0.045f, 0.055f, 0.09f),
            Vector2.zero, Vector2.one);
        background.transform.SetAsFirstSibling();

        CreateText(canvas.transform, "Title",
            "SPEEDRUN LAB",
            30, new Vector2(0.04f, 0.88f),
            new Vector2(0.96f, 0.97f),
            TextAnchor.MiddleLeft);

        sortDropdown = CreateDropdown("Sort",
            new[] { "Fastest first", "Slowest first", "File order" },
            new Vector2(0.04f, 0.75f), new Vector2(0.25f, 0.81f));

        bastionDropdown = CreateDropdown("Bastion",
            new[] { "All" },
            new Vector2(0.28f, 0.75f), new Vector2(0.48f, 0.81f));

        overworldDropdown = CreateDropdown("Overworld",
            new[] { "All" },
            new Vector2(0.51f, 0.75f), new Vector2(0.71f, 0.81f));

        playButton = CreateButton("Replay animation",
            new Vector2(0.75f, 0.75f), new Vector2(0.96f, 0.81f),
            () => StartAnimation());

        CreateButton("Show distribution",
            new Vector2(0.75f, 0.67f), new Vector2(0.96f, 0.73f),
            () =>
            {
                StopAnimation();
                viewMode = ViewMode.Distribution;
                BuildHistogram();
            });

        CreateButton("Show runs",
            new Vector2(0.51f, 0.67f), new Vector2(0.71f, 0.73f),
            () =>
            {
                viewMode = ViewMode.Runs;
                StartAnimation();
            });

        chart = CreatePanel(canvas.transform, "Chart",
            new Color(0.075f, 0.09f, 0.14f),
            new Vector2(0.04f, 0.15f),
            new Vector2(0.96f, 0.64f));

        statusText = CreateText(canvas.transform, "Status",
            "Loading runs...",
            17, new Vector2(0.04f, 0.06f),
            new Vector2(0.96f, 0.13f),
            TextAnchor.MiddleLeft);

        sortDropdown.onValueChanged.AddListener(_ =>
            ApplyFiltersAndSort());
    }

    private void PopulateFilters()
    {
        SetDropdownOptions(bastionDropdown, "All",
            allRuns.Select(r => r.bastionType));
        SetDropdownOptions(overworldDropdown, "All",
            allRuns.Select(r => r.overworldType));

        bastionDropdown.onValueChanged.AddListener(_ =>
            ApplyFiltersAndSort());
        overworldDropdown.onValueChanged.AddListener(_ =>
            ApplyFiltersAndSort());
    }

    private void SetDropdownOptions(Dropdown dropdown, string allLabel,
        IEnumerable<string> values)
    {
        dropdown.ClearOptions();
        List<string> options = new() { allLabel };
        options.AddRange(values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v));
        dropdown.AddOptions(options);
    }

    private Dropdown CreateDropdown(string objectName, string[] options,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(objectName,
            typeof(RectTransform), typeof(Image), typeof(Dropdown));
        go.transform.SetParent(canvas.transform, false);
        SetAnchors(go.GetComponent<RectTransform>(), anchorMin, anchorMax);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.10f, 0.13f, 0.20f);

        Dropdown dropdown = go.GetComponent<Dropdown>();
        Text label = CreateText(go.transform, "Label", options[0],
            15, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
        label.rectTransform.offsetMin = new Vector2(6, 2);
        label.rectTransform.offsetMax = new Vector2(-22, -2);

        GameObject template = new GameObject("Template",
            typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        template.transform.SetParent(go.transform, false);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = new Vector2(0, 2);
        templateRect.sizeDelta = new Vector2(0, 180);
        template.SetActive(false);

        Image templateImage = template.GetComponent<Image>();
        templateImage.color = new Color(0.10f, 0.13f, 0.20f);

        GameObject viewport = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 30);

        GameObject item = new GameObject("Item",
        typeof(RectTransform), typeof(Toggle), typeof(Image));

        Image itemBackground = item.GetComponent<Image>();
        itemBackground.color = new Color(0.10f, 0.13f, 0.20f);

        item.transform.SetParent(content.transform, false);
        item.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 30);

        Text itemText = CreateText(item.transform, "Item Label", "Option",
            14, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft);
        itemText.color = new Color(0.92f, 0.94f, 1f);
        itemText.fontSize = 14;
        itemText.rectTransform.offsetMin = new Vector2(8, 0);

        Toggle toggle = item.GetComponent<Toggle>();
        toggle.targetGraphic = itemBackground;
        toggle.graphic = null;

        ColorBlock colors = toggle.colors;
        colors.normalColor = new Color(0.10f, 0.13f, 0.20f);
        colors.highlightedColor = new Color(0.20f, 0.30f, 0.45f);
        colors.pressedColor = new Color(0.25f, 0.40f, 0.60f);
        colors.selectedColor = new Color(0.16f, 0.24f, 0.36f);
        colors.colorMultiplier = 1f;
        toggle.colors = colors;

        ScrollRect scroll = template.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;

        dropdown.template = templateRect;
        dropdown.captionText = label;
        dropdown.itemText = itemText;
        dropdown.targetGraphic = image;
        dropdown.options.Clear();
        dropdown.AddOptions(options.ToList());
        dropdown.value = 0;
        dropdown.RefreshShownValue();

        return dropdown;
    }

    private Button CreateButton(string label, Vector2 anchorMin,
        Vector2 anchorMax, Action action)
    {
        GameObject go = new GameObject(label,
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);
        SetAnchors(go.GetComponent<RectTransform>(), anchorMin, anchorMax);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.2f, 0.38f, 0.65f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());

        CreateText(go.transform, "Label", label, 15,
            Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);

        return button;
    }

    private Image CreateImage(Transform parent, string objectName,
        Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(objectName,
            typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), anchorMin, anchorMax);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private RectTransform CreatePanel(Transform parent, string objectName,
        Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        Image image = CreateImage(parent, objectName, color,
            anchorMin, anchorMax);
        return image.rectTransform;
    }

    private Text CreateText(Transform parent, string objectName,
        string value, int fontSize, Vector2 anchorMin,
        Vector2 anchorMax, TextAnchor alignment)
    {
        GameObject go = new GameObject(objectName,
            typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), anchorMin, anchorMax);

        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = new Color(0.9f, 0.93f, 1f);
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void SetAnchors(RectTransform rect,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
