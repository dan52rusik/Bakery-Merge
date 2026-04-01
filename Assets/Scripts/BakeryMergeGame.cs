using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using YG;

public sealed class BakeryMergeGame : MonoBehaviour
{
    [Header("Art Assets")]
    public Texture2D cakesAtlasTexture;
    
    [Header("UI Customization")]
    public Font customUiFont;

    private const float ArenaHalfWidth = 3.6f;
    private const float ArenaBottom = -4.15f;
    private const float ArenaTop = 4.1f;
    private const float SpawnY = 3.35f;
    private const float WarningY = 2.95f;
    private const float GameOverDelay = 1.35f;
    private const float SaveDebounceDelay = 1.2f;
    private const string ScoreLeaderboardName = "BakeryScore";

    private readonly List<SweetDefinition> definitions = new();
    private readonly List<BakeryMergeItem> items = new();
    private PhysicsMaterial2D softPhysicsMaterial;
    private Sprite[] sweetSprites;
    private bool useCustomSweetSprites;
    private Sprite circleSprite;
    private Sprite sphereSprite;
    private Sprite squareSprite;
    private Sprite[] cakeSprites;
    private Camera mainCamera;
    private SpriteRenderer warningLineRenderer;
    private SpriteRenderer previewGuideRenderer;
    private Transform previewRoot;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle warningStyle;
    private GUIStyle buttonStyle;
    private GUIStyle centerStyle;
    private GUIStyle miniStyle;
    private Font uiFont;

    private Sprite roundedBoxSprite;
    private Texture2D russianFlagTexture;
    private Texture2D englishFlagTexture;
    private Texture2D telegramIconTexture;
    private Queue<BakeryMergeItem>[] sweetPools;
    private readonly Queue<ParticleSystem> sugarBurstPool = new();

    private BoosterMode activeBooster;
    private int nextSpawnLevel;
    private int score;
    private int coins;
    private float overflowTimer;
    private int highestLevelReached;
    private int bestScore;
    private bool gameOver;
    private float nextDropReadyTime;
    private bool suppressSaveEvents;
    private bool savePending;
    private bool saveLeaderboardPending;
    private float nextSaveTime;
    private bool touchDropBlockedByUi;

    private int[] inventoryCounts;
    private bool[] discoveredLevels;
    private int discoveredCount;

    private const float DropCooldown = 0.33f;
    private const int CookieRemoveCost = 8;
    private const int ShuffleCost = 12;
    private const int UpgradeCost = 15;

    private BakeryMergeItem hoveredItem;
    private Vector2 lastHoverMousePosition = new(float.MinValue, float.MinValue);
    private bool isShopOpen = false;
    private bool isGameStarted = false;
    private bool IsRussianLanguage => string.Equals(YG2.lang, "ru", StringComparison.OrdinalIgnoreCase);

    private void Awake()
    {
        BuildDefinitions();
        inventoryCounts = new int[definitions.Count];
        discoveredLevels = new bool[definitions.Count];
        sweetPools = new Queue<BakeryMergeItem>[definitions.Count];
        for (var i = 0; i < sweetPools.Length; i++)
        {
            sweetPools[i] = new Queue<BakeryMergeItem>();
        }
        BuildSharedAssets();
        ConfigureCamera();
        ConfigurePhysics();
        BuildBackdrop();
        BuildArena();
    }

    private void OnEnable()
    {
        YG2.onGetSDKData += HandleSdkDataLoaded;
    }

    private void OnDisable()
    {
        YG2.onGetSDKData -= HandleSdkDataLoaded;
    }

    private void Start()
    {
        HandleSdkDataLoaded();
        if (!YG2.saves.bakeryHasSave)
        {
            ChooseNextSpawnLevel();
        }
    }

    private void Update()
    {
        FlushPendingSaveIfNeeded();

        if (!isGameStarted)
        {
            return;
        }

        if (gameOver)
        {
            if (WasRestartPressed())
            {
                RestartGame();
            }

            return;
        }

        UpdateHover();
        UpdatePreviewPosition();
        HandleKeyboardShortcuts();
        HandlePointerInput();
        UpdateOverflowState();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            FlushPendingSaveImmediate();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            FlushPendingSaveImmediate();
        }
    }

    private void OnGUI()
    {
        EnsureGuiStyles();

        if (!isGameStarted)
        {
            DrawMainMenu();
            return;
        }
        
        // --- 1. Top Header (Score, Progress, Coins) ---
        var headerRect = GetHeaderRect();
        DrawModernPanel(headerRect, new Color(1f, 1f, 1f, 0.88f));
        
        GUILayout.BeginArea(headerRect);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        var headerValueStyle = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };

        // 1. Score
        GUILayout.BeginVertical(GUILayout.Width(headerRect.width * 0.3f));
        GUILayout.Space(12f);
        GUILayout.Label(Localize("СЧЁТ", "SCORE"), miniStyle, GUILayout.Height(14f));
        GUILayout.Label($"{score}", headerValueStyle, GUILayout.Height(32f));
        GUILayout.EndVertical();
        
        // 2. Collection Progress
        GUILayout.BeginVertical(GUILayout.Width(headerRect.width * 0.3f));
        GUILayout.Space(12f);
        GUILayout.Label(Localize("ОТКРЫТО", "COLLECTED"), miniStyle, GUILayout.Height(14f));
        GUILayout.Label($"{discoveredCount}/{definitions.Count}", headerValueStyle, GUILayout.Height(32f));
        GUILayout.EndVertical();
        
        // 3. Coins
        GUILayout.BeginVertical(GUILayout.Width(headerRect.width * 0.3f));
        GUILayout.Space(12f);
        GUILayout.Label(Localize("МОНЕТЫ", "COINS"), miniStyle, GUILayout.Height(14f));
        GUILayout.Label($"● {coins}", headerValueStyle, GUILayout.Height(32f));
        GUILayout.EndVertical();
        
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();



        // --- 2. Side Panel (Inventory/Shop) ---
        if (isShopOpen)
        {
            var sidePanelRect = GetShopRect();
            var sidePanelWidth = sidePanelRect.width;
            DrawModernPanel(sidePanelRect, new Color(1f, 0.96f, 0.94f, 0.95f));
            
            GUILayout.BeginArea(sidePanelRect);
            GUILayout.Space(16f);
            GUILayout.Label(Localize(" ПРЕДМЕТЫ", " ITEMS"), titleStyle);
            GUILayout.Space(12f);
            
            var hasItems = false;
            for (var i = definitions.Count - 1; i >= 0; i--)
            {
                if (inventoryCounts[i] <= 0) continue;
                hasItems = true;
                
                var rect = GUILayoutUtility.GetRect(sidePanelWidth - 32f, 48f);
                DrawModernPanel(rect, new Color(0.9f, 0.82f, 0.78f, 0.6f));
                
                var nameRect = new Rect(rect.x + 16f, rect.y, 160f, rect.height);
                var btnRect = new Rect(rect.xMax - 68f, rect.y + 12f, 52f, 24f);
                
                GUI.Label(nameRect, $"{definitions[i].Name}  <color=#885544>x{inventoryCounts[i]}</color>", bodyStyle);
                
                GUI.backgroundColor = new Color(0.88f, 0.84f, 0.82f, 0.95f);
                if (GUI.Button(btnRect, $"+{definitions[i].SellPrice}", buttonStyle))
                {
                    SellOne(i);
                }
                GUI.backgroundColor = Color.white;
                
                GUILayout.Space(8f);
            }

            if (!hasItems)
            {
                GUILayout.Space(8f);
                GUILayout.Label(Localize(" <color=#886655>Пока нечего продавать.\n\n Выполняйте слияния, чтобы открывать новые предметы!</color>", " <color=#886655>Nothing to sell yet.\n\n Merge sweets to unlock new items!</color>"), bodyStyle);
            }
            GUILayout.EndArea();
        }

        // --- 3. Bottom Dock (Boosters & Actions) ---
        var dockRect = GetDockRect();
        DrawModernPanel(dockRect, new Color(0.18f, 0.08f, 0.06f, 0.96f));
        
        GUILayout.BeginArea(dockRect);
        GUILayout.Space(12f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        // 1-3. Boosters
        var canRemove = coins >= CookieRemoveCost;
        GUI.enabled = canRemove || activeBooster == BoosterMode.Remove;
        DrawBoosterButton(Localize("ЛОПАТКА", "SPATULA"), CookieRemoveCost, "1", () => activeBooster = BoosterMode.Remove, activeBooster == BoosterMode.Remove, canRemove);
        GUILayout.Space(8f);
        
        var canShuffle = coins >= ShuffleCost;
        GUI.enabled = canShuffle;
        DrawBoosterButton(Localize("МИКСЕР", "MIXER"), ShuffleCost, "2", ActivateShuffle, false, canShuffle);
        GUILayout.Space(8f);
        
        var canUpgrade = coins >= UpgradeCost;
        GUI.enabled = canUpgrade || activeBooster == BoosterMode.Upgrade;
        DrawBoosterButton(Localize("МЕШОК", "PIPING BAG"), UpgradeCost, "3", () => activeBooster = BoosterMode.Upgrade, activeBooster == BoosterMode.Upgrade, canUpgrade);
        GUI.enabled = true;

        GUILayout.Space(16f);
        // Vertical Divider
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        GUILayout.Box(GUIContent.none, GUILayout.Width(2f), GUILayout.ExpandHeight(true));
        GUI.color = Color.white;
        GUILayout.Space(16f);

        // 4. Shop Button
        GUI.backgroundColor = isShopOpen ? new Color(0.85f, 0.75f, 0.65f, 0.95f) : new Color(1f, 0.94f, 0.88f, 1f);
        if (GUILayout.Button(isShopOpen ? Localize("ОК", "OK") : Localize("ПРИЛАВОК", "SHOP"), buttonStyle, GUILayout.Width(80f), GUILayout.Height(52f)))
        {
            isShopOpen = !isShopOpen;
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(8f);

        // 5. Reward Button
        GUI.backgroundColor = new Color(1f, 0.85f, 0.35f, 1f);
        if (GUILayout.Button(Localize("СМОТРЕТЬ\nРЕКЛАМУ\n+10 МОНЕТ", "WATCH AD\n+10 COINS"), buttonStyle, GUILayout.Width(92f), GUILayout.Height(58f)))
        {
            YG2.RewardedAdvShow("FreeCoins", () =>
            {
                coins += 10;
                SaveProgressState();
            });
        }
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // Hint for active booster
        if (activeBooster != BoosterMode.None)
        {
            var hintRect = GetBoosterHintRect();
            DrawModernPanel(hintRect, new Color(1f, 0.85f, 0.4f, 0.98f));
            var hintStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(hintRect, Localize($"Активно: {TranslateBooster(activeBooster)} - выберите цель", $"Active: {TranslateBooster(activeBooster)} - select target"), hintStyle);
        }

        if (overflowTimer > 0f)
        {
            var danger = Mathf.Clamp01(overflowTimer / GameOverDelay);
            var warnRect = GetWarningRect();
            GUI.color = new Color(1f, 0.2f, 0.1f, 0.4f + Mathf.PingPong(Time.time * 2f, 0.5f));
            GUI.DrawTexture(warnRect, roundedBoxSprite.texture);
            GUI.color = Color.white;
            GUI.Label(warnRect, $"{Localize(" ПЕРЕПОЛНЕНИЕ", " OVERFLOW")}: {danger:P0}", warningStyle);
        }

        if (!gameOver) return;

        var overlayRect = new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f - 140f, 480f, 280f);
        DrawModernPanel(overlayRect, new Color(0.15f, 0.05f, 0.05f, 0.98f));

        GUILayout.BeginArea(overlayRect);
        GUILayout.Space(32f);
        GUILayout.Label(Localize("ПОДНОС ПЕРЕПОЛНЕН!", "TRAY OVERFLOWED!"), centerStyle, GUILayout.Height(48f));
        GUILayout.Space(16f);
        GUILayout.Label($"{Localize("СЧЁТ", "SCORE")}: <color=yellow>{score}</color>", centerStyle);
        var oldAlign = bodyStyle.alignment;
        bodyStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label($"{Localize("ЛУЧШИЙ ДЕСЕРТ", "BEST DESSERT")}: {definitions[highestLevelReached].Name}", bodyStyle);
        bodyStyle.alignment = oldAlign;
        GUILayout.Space(32f);
        
        GUI.backgroundColor = new Color(0.95f, 0.85f, 0.75f, 1f);
        if (GUILayout.Button(Localize("ИГРАТЬ СНОВА", "PLAY AGAIN"), buttonStyle, GUILayout.Height(48f)))
        {
            RestartGame();
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndArea();
    }

    private void DrawModernPanel(Rect r, Color c)
    {
        var oldColor = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, roundedBoxSprite.texture);
        GUI.color = oldColor;
    }

    private static Rect GetHeaderRect()
    {
        var width = Mathf.Min(Screen.width * 0.95f, 450f);
        return new Rect(Screen.width * 0.5f - width * 0.5f, 16f, width, 84f);
    }

    private static Rect GetDockRect()
    {
        var width = Mathf.Min(Screen.width * 0.98f, 520f);
        return new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height - 104f, width, 88f);
    }

    private static Rect GetShopRect()
    {
        var width = Mathf.Min(Screen.width * 0.9f, 320f);
        return new Rect(Screen.width * 0.5f - width * 0.5f, 112f, width, Screen.height - 300f);
    }

    private static Rect GetBoosterHintRect()
    {
        return new Rect(Screen.width * 0.5f - 150f, Screen.height - 150f, 300f, 32f);
    }

    private static Rect GetWarningRect()
    {
        return new Rect(Screen.width * 0.5f - 180f, 104f, 360f, 40f);
    }

    private void DrawBoosterButton(string title, int cost, string key, Action onClick, bool isSelected, bool canAfford)
    {
        var buttonRect = GUILayoutUtility.GetRect(90f, 48f, GUILayout.Width(90f), GUILayout.Height(48f));
        
        GUI.backgroundColor = isSelected ? new Color(1f, 0.85f, 0.65f, 1f) : new Color(0.95f, 0.9f, 0.85f, 0.9f);
        if (GUI.Button(buttonRect, string.Empty, buttonStyle))
        {
            onClick?.Invoke();
        }
        GUI.backgroundColor = Color.white;
        
        var oldAlign = miniStyle.alignment;
        miniStyle.alignment = TextAnchor.MiddleCenter;
        
        var titleRect = new Rect(buttonRect.x, buttonRect.y + 6f, 90f, 20f);
        GUI.Label(titleRect, title, miniStyle);
        
        var costRect = new Rect(buttonRect.x, buttonRect.y + 24f, 90f, 20f);
        var costColor = canAfford ? "#A05533" : "#C05544";
        GUI.Label(costRect, $"<color={costColor}>○ {cost}</color>", miniStyle);
        
        var keyLabelRect = new Rect(buttonRect.x, buttonRect.y - 18f, 90f, 18f);
        GUI.Label(keyLabelRect, $"[{key}]", miniStyle);
        
        miniStyle.alignment = oldAlign;
    }

    private void BuildDefinitions()
    {
        definitions.Clear();
        definitions.Add(new SweetDefinition(this, "Капля теста", "Dough Drop", new Color(0.98f, 0.9f, 0.63f), new Color(0.82f, 0.62f, 0.31f), 0.34f, 1));
        definitions.Add(new SweetDefinition(this, "Формочка с тестом", "Batter Cup", new Color(0.98f, 0.76f, 0.46f), new Color(0.59f, 0.32f, 0.17f), 0.42f, 2));
        definitions.Add(new SweetDefinition(this, "Пышный бисквит", "Fluffy Sponge", new Color(0.97f, 0.8f, 0.44f), new Color(0.72f, 0.46f, 0.19f), 0.5f, 4));
        definitions.Add(new SweetDefinition(this, "Бисквитные коржи", "Cake Layers", new Color(0.96f, 0.72f, 0.52f), new Color(0.88f, 0.95f, 0.91f), 0.58f, 7));
        definitions.Add(new SweetDefinition(this, "Кусочек торта", "Cake Slice", new Color(0.98f, 0.54f, 0.66f), new Color(0.99f, 0.95f, 0.96f), 0.68f, 11));
        definitions.Add(new SweetDefinition(this, "Пончик", "Donut", new Color(0.94f, 0.51f, 0.72f), new Color(1f, 0.88f, 0.44f), 0.78f, 16));
        definitions.Add(new SweetDefinition(this, "Шоколадный капкейк", "Chocolate Cupcake", new Color(0.48f, 0.25f, 0.17f), new Color(0.9f, 0.16f, 0.24f), 0.9f, 22));
        definitions.Add(new SweetDefinition(this, "Праздничный торт", "Celebration Cake", new Color(0.86f, 0.95f, 1f), new Color(0.95f, 0.26f, 0.51f), 1.06f, 30));
        definitions.Add(new SweetDefinition(this, "Свадебный торт", "Wedding Cake", new Color(1f, 0.98f, 0.99f), new Color(0.89f, 0.79f, 0.42f), 1.24f, 45));
    }

    private void BuildSharedAssets()
    {
        if (cakesAtlasTexture == null)
        {
            cakesAtlasTexture = LoadAtlasTexture("torts");
        }

        circleSprite = CreateCircleSprite(128);
        sphereSprite = CreateSphereSprite(128);
        squareSprite = CreateSquareSprite();
        roundedBoxSprite = CreateRoundedBoxSprite(128, 32);
        russianFlagTexture = CreateRussianFlagTexture(96, 64);
        englishFlagTexture = CreateUsFlagTexture(96, 64);
        telegramIconTexture = LoadAtlasTexture("telegram");

        cakeSprites = new Sprite[9];
        if (cakesAtlasTexture != null)
        {
            int w = cakesAtlasTexture.width / 3;
            int h = cakesAtlasTexture.height / 3;
            for (int i = 0; i < 9; i++)
            {
                int row = i / 3;
                int col = i % 3;
                int yPix = (2 - row) * h;
                int xPix = col * w;
                cakeSprites[i] = CreateCleanCellSprite(cakesAtlasTexture, xPix, yPix, w, h);
            }
        }
        else
        {
            for (int i = 0; i < 9; i++) cakeSprites[i] = sphereSprite;
        }

        softPhysicsMaterial = new PhysicsMaterial2D("BakerySoftMaterial")
        {
            friction = 0.18f,
            bounciness = 0.22f
        };
    }

    private void ConfigureCamera()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
        }

        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = new Color(0.99f, 0.92f, 0.87f);
        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 6.2f;
        mainCamera.transform.position = new Vector3(0f, 0.2f, -10f);
    }

    private void ConfigurePhysics()
    {
        Physics2D.gravity = new Vector2(0f, -16f);
    }

    private void BuildBackdrop()
    {
        CreatePanel(
            "Backdrop",
            new Vector3(0f, 0.1f, 8f),
            new Vector3(12f, 14f, 1f),
            new Color(0.99f, 0.93f, 0.9f));

        CreatePanel(
            "BackdropStripeLeft",
            new Vector3(-4.8f, 0.1f, 7.8f),
            new Vector3(1.25f, 14f, 1f),
            new Color(0.96f, 0.9f, 0.87f, 0.92f));

        CreatePanel(
            "BackdropStripeRight",
            new Vector3(4.8f, 0.1f, 7.8f),
            new Vector3(1.25f, 14f, 1f),
            new Color(0.96f, 0.9f, 0.87f, 0.92f));

        CreateCircleAccent("BackGlowPink", new Vector3(-2.5f, 3.8f, 7.4f), 2.1f, new Color(1f, 0.76f, 0.84f, 0.18f));
        CreateCircleAccent("BackGlowCream", new Vector3(2.7f, 2.8f, 7.4f), 2.6f, new Color(1f, 0.93f, 0.74f, 0.16f));

        CreatePanel(
            "Counter",
            new Vector3(0f, ArenaBottom - 0.75f, 7f),
            new Vector3(10f, 1.7f, 1f),
            new Color(0.69f, 0.44f, 0.29f));

        CreatePanel(
            "CounterFront",
            new Vector3(0f, ArenaBottom - 1.1f, 6.9f),
            new Vector3(10f, 1.05f, 1f),
            new Color(0.57f, 0.32f, 0.2f));

        CreatePanel(
            "TrayFill",
            new Vector3(0f, 0.05f, 6f),
            new Vector3(ArenaHalfWidth * 2f - 0.3f, ArenaTop - ArenaBottom - 0.3f, 1f),
            new Color(1f, 0.99f, 0.97f, 0.94f));

        CreatePanel(
            "GlassShine",
            new Vector3(-1.2f, 0.6f, 5.8f),
            new Vector3(0.36f, 8.8f, 1f),
            new Color(1f, 1f, 1f, 0.16f));
    }

    private void BuildArena()
    {
        CreateWall("Floor", new Vector2(0f, ArenaBottom - 0.15f), new Vector2(ArenaHalfWidth * 2f + 0.6f, 0.4f));
        CreateWall("LeftWall", new Vector2(-ArenaHalfWidth - 0.15f, 0.1f), new Vector2(0.4f, ArenaTop - ArenaBottom + 0.8f));
        CreateWall("RightWall", new Vector2(ArenaHalfWidth + 0.15f, 0.1f), new Vector2(0.4f, ArenaTop - ArenaBottom + 0.8f));
        warningLineRenderer = CreatePanel("WarningLine", new Vector3(0f, WarningY, 5f), new Vector3(ArenaHalfWidth * 2f - 0.2f, 0.05f, 1f), new Color(0.96f, 0.61f, 0.56f, 0.75f));
        previewGuideRenderer = CreatePanel("PreviewGuide", new Vector3(0f, 0.1f, 5.2f), new Vector3(0.06f, ArenaTop - ArenaBottom - 0.2f, 1f), new Color(0.91f, 0.56f, 0.64f, 0.28f));
    }

    private void HandleKeyboardShortcuts()
    {
        if (WasDigitPressed(Digit.Digit1))
        {
            activeBooster = BoosterMode.Remove;
        }

        if (WasDigitPressed(Digit.Digit2))
        {
            ActivateShuffle();
        }

        if (WasDigitPressed(Digit.Digit3))
        {
            activeBooster = BoosterMode.Upgrade;
        }

        if (WasEscapePressed() || WasRightMousePressed())
        {
            activeBooster = BoosterMode.None;
        }
    }

    private void HandlePointerInput()
    {
        if (Touchscreen.current != null)
        {
            HandleTouchPointerInput();
            return;
        }

        if (!WasPrimaryPointerPressed())
        {
            return;
        }

        var pointerPosition = GetPointerScreenPosition();
        if (!pointerPosition.HasValue)
        {
            return;
        }

        var guiPoint = new Vector2(pointerPosition.Value.x, Screen.height - pointerPosition.Value.y);
        if (IsPointerOverUi(guiPoint))
        {
            return;
        }

        if (activeBooster != BoosterMode.None)
        {
            if (hoveredItem != null)
            {
                ApplyBoosterToHovered();
            }

            return;
        }

        if (Time.time < nextDropReadyTime)
        {
            return;
        }

        var pointerWorld = mainCamera.ScreenToWorldPoint(pointerPosition.Value);
        var spawnX = Mathf.Clamp(pointerWorld.x, -ArenaHalfWidth + 0.55f, ArenaHalfWidth - 0.55f);
        SpawnSweet(nextSpawnLevel, new Vector2(spawnX, SpawnY));
        nextDropReadyTime = Time.time + DropCooldown;
        ChooseNextSpawnLevel();
        SaveProgressState();
    }

    private void HandleTouchPointerInput()
    {
        var touch = Touchscreen.current?.primaryTouch;
        if (touch == null)
        {
            return;
        }

        var pointerPosition = touch.position.ReadValue();
        var guiPoint = new Vector2(pointerPosition.x, Screen.height - pointerPosition.y);

        if (touch.press.wasPressedThisFrame)
        {
            touchDropBlockedByUi = IsPointerOverUi(guiPoint);

            if (touchDropBlockedByUi)
            {
                return;
            }

            if (activeBooster != BoosterMode.None && hoveredItem != null)
            {
                ApplyBoosterToHovered();
                touchDropBlockedByUi = true;
            }

            return;
        }

        if (!touch.press.wasReleasedThisFrame)
        {
            return;
        }

        var releaseOverUi = IsPointerOverUi(guiPoint);
        var shouldBlockDrop = touchDropBlockedByUi || releaseOverUi;
        touchDropBlockedByUi = false;

        if (shouldBlockDrop)
        {
            return;
        }

        if (activeBooster != BoosterMode.None)
        {
            if (hoveredItem != null)
            {
                ApplyBoosterToHovered();
            }

            return;
        }

        if (Time.time < nextDropReadyTime)
        {
            return;
        }

        var pointerWorld = mainCamera.ScreenToWorldPoint(pointerPosition);
        var spawnX = Mathf.Clamp(pointerWorld.x, -ArenaHalfWidth + 0.55f, ArenaHalfWidth - 0.55f);
        SpawnSweet(nextSpawnLevel, new Vector2(spawnX, SpawnY));
        nextDropReadyTime = Time.time + DropCooldown;
        ChooseNextSpawnLevel();
        SaveProgressState();
    }

    private void UpdateHover()
    {
        var pointerPosition = GetPointerScreenPosition();
        if (!pointerPosition.HasValue)
        {
            if (hoveredItem != null)
            {
                hoveredItem.SetHighlight(false);
                hoveredItem = null;
            }
            return;
        }

        if ((pointerPosition.Value - lastHoverMousePosition).sqrMagnitude < 0.25f)
        {
            return;
        }

        lastHoverMousePosition = pointerPosition.Value;

        var pointerWorld = mainCamera.ScreenToWorldPoint(pointerPosition.Value);
        var hit = Physics2D.Raycast(pointerWorld, Vector2.zero);
        var newHoveredItem = hit.collider == null ? null : hit.collider.GetComponent<BakeryMergeItem>();

        if (hoveredItem == newHoveredItem)
        {
            return;
        }

        if (hoveredItem != null)
        {
            hoveredItem.SetHighlight(false);
        }

        hoveredItem = newHoveredItem;

        if (hoveredItem != null)
        {
            hoveredItem.SetHighlight(true);
        }
    }

    private void UpdateOverflowState()
    {
        var isOverflowing = false;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] == null)
            {
                items.RemoveAt(i);
                continue;
            }

            if (items[i].IsOverflowThreat(WarningY))
            {
                isOverflowing = true;
            }
        }

        overflowTimer = isOverflowing ? overflowTimer + Time.deltaTime : 0f;

        if (warningLineRenderer != null)
        {
            var pulse = 0.42f + Mathf.PingPong(Time.time * 2.4f, 0.28f);
            var danger = Mathf.Clamp01(overflowTimer / GameOverDelay);
            warningLineRenderer.color = Color.Lerp(
                new Color(0.96f, 0.61f, 0.56f, 0.58f),
                new Color(0.83f, 0.1f, 0.18f, pulse + 0.12f),
                danger);
        }

        if (overflowTimer >= GameOverDelay)
        {
            TriggerGameOver();
        }
    }

    private static Vector2? GetPointerScreenPosition()
    {
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;
            if (touch.press.isPressed || touch.press.wasPressedThisFrame || touch.press.wasReleasedThisFrame)
            {
                return touch.position.ReadValue();
            }
        }

        return Mouse.current == null ? null : Mouse.current.position.ReadValue();
    }

    private static bool WasPrimaryPointerPressed()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private static bool WasRightMousePressed()
    {
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private static bool WasRestartPressed()
    {
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    }

    private static bool WasDigitPressed(Digit digit)
    {
        if (Keyboard.current == null)
        {
            return false;
        }

        return digit switch
        {
            Digit.Digit1 => Keyboard.current.digit1Key.wasPressedThisFrame,
            Digit.Digit2 => Keyboard.current.digit2Key.wasPressedThisFrame,
            Digit.Digit3 => Keyboard.current.digit3Key.wasPressedThisFrame,
            Digit.Digit4 => Keyboard.current.digit4Key.wasPressedThisFrame,
            _ => false
        };
    }

    private void UpdatePreviewPosition()
    {
        if (previewRoot == null || previewGuideRenderer == null)
        {
            return;
        }

        var mousePosition = GetPointerScreenPosition();
        var previewX = 0f;
        if (mousePosition.HasValue)
        {
            var pointerWorld = mainCamera.ScreenToWorldPoint(mousePosition.Value);
            previewX = Mathf.Clamp(pointerWorld.x, -ArenaHalfWidth + 0.55f, ArenaHalfWidth - 0.55f);
        }

        previewRoot.position = new Vector3(previewX, SpawnY + 0.78f, 0f);
        previewGuideRenderer.transform.position = new Vector3(previewX, 0.1f, 5.2f);
    }

    private void HandleSdkDataLoaded()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        LoadFromSaves();
    }

    private void ApplyBoosterToHovered()
    {
        switch (activeBooster)
        {
            case BoosterMode.Remove:
                if (coins >= CookieRemoveCost) coins -= CookieRemoveCost;
                else break;
                SpawnSugarBurst(hoveredItem.transform.position, new Color(1f, 0.95f, 0.95f));
                RemoveItem(hoveredItem);
                activeBooster = BoosterMode.None;
                SaveProgressState();
                break;
            case BoosterMode.Upgrade:
                if (coins >= UpgradeCost) coins -= UpgradeCost;
                else break;
                UpgradeItem(hoveredItem);
                activeBooster = BoosterMode.None;
                SaveProgressState();
                break;
        }
    }

    private void ActivateShuffle()
    {
        if (items.Count == 0) return;
        
        if (coins >= ShuffleCost) coins -= ShuffleCost;
        else return;

        activeBooster = BoosterMode.None;

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            var randomForce = new Vector2(UnityEngine.Random.Range(-1.8f, 1.8f), UnityEngine.Random.Range(1.2f, 2.6f));
            item.Body.linearVelocity = Vector2.zero;
            item.Body.AddForce(randomForce, ForceMode2D.Impulse);
            item.Body.AddTorque(UnityEngine.Random.Range(-12f, 12f), ForceMode2D.Impulse);
        }

        SaveProgressState();
    }

    private void UpgradeItem(BakeryMergeItem item)
    {
        if (item == null)
        {
            return;
        }

        if (item.Level >= definitions.Count - 1)
        {
            SpawnSugarBurst(item.transform.position, new Color(1f, 0.96f, 0.75f));
            return;
        }

        var nextLevel = item.Level + 1;
        var position = item.transform.position;
        RemoveItem(item);
        SpawnSugarBurst(position, definitions[Mathf.Min(definitions.Count - 1, nextLevel)].AccentColor);
        SpawnSweet(nextLevel, position);
        RegisterCraft(nextLevel);
    }

    private void RemoveItem(BakeryMergeItem item)
    {
        if (item == null)
        {
            return;
        }

        items.Remove(item);
        item.PrepareForPooling();
        item.gameObject.SetActive(false);
        sweetPools[item.Level].Enqueue(item);
    }

    private void SaveProgressState(bool submitLeaderboard = false)
    {
        if (suppressSaveEvents)
        {
            return;
        }

        savePending = true;
        saveLeaderboardPending |= submitLeaderboard;
        nextSaveTime = Time.unscaledTime + SaveDebounceDelay;
    }

    private void FlushPendingSaveIfNeeded()
    {
        if (!savePending)
        {
            return;
        }

        if (Time.unscaledTime < nextSaveTime)
        {
            return;
        }

        FlushPendingSaveImmediate();
    }

    private void FlushPendingSaveImmediate()
    {
        if (!savePending || suppressSaveEvents)
        {
            return;
        }

        YG2.saves.bakeryHasSave = true;
        YG2.saves.bakeryScore = score;
        YG2.saves.bakeryCoins = coins;
        YG2.saves.bakeryHighestLevelReached = highestLevelReached;
        YG2.saves.bakeryBestScore = bestScore;
        YG2.saves.bakeryNextSpawnLevel = nextSpawnLevel;
        YG2.saves.bakeryDiscoveredCount = discoveredCount;
        YG2.saves.bakeryInventoryCounts = (int[])inventoryCounts.Clone();
        YG2.saves.bakeryDiscoveredLevels = (bool[])discoveredLevels.Clone();
        YG2.saves.bakeryItems = CaptureBoardState();

        if (saveLeaderboardPending)
        {
            SubmitLeaderboard();
        }

        savePending = false;
        saveLeaderboardPending = false;
        YG2.SaveProgress();
    }

    private YG.BakerySavedItemData[] CaptureBoardState()
    {
        var savedItems = new List<YG.BakerySavedItemData>();
        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            savedItems.Add(new YG.BakerySavedItemData
            {
                level = item.Level,
                posX = item.transform.position.x,
                posY = item.transform.position.y,
                rotationZ = item.transform.eulerAngles.z
            });
        }

        return savedItems.ToArray();
    }

    private void LoadFromSaves()
    {
        suppressSaveEvents = true;

        try
        {
            ClearBoard();

            score = 0;
            coins = 0;
            overflowTimer = 0f;
            highestLevelReached = 0;
            bestScore = YG2.saves.bakeryBestScore;
            discoveredCount = 0;
            activeBooster = BoosterMode.None;
            hoveredItem = null;
            gameOver = false;
            nextDropReadyTime = 0f;

            Array.Fill(inventoryCounts, 0);
            Array.Fill(discoveredLevels, false);

            if (YG2.saves.bakeryHasSave)
            {
                score = YG2.saves.bakeryScore;
                coins = YG2.saves.bakeryCoins;
                highestLevelReached = Mathf.Clamp(YG2.saves.bakeryHighestLevelReached, 0, definitions.Count - 1);
                bestScore = Mathf.Max(bestScore, YG2.saves.bakeryBestScore);
                nextSpawnLevel = Mathf.Clamp(YG2.saves.bakeryNextSpawnLevel, 0, Mathf.Min(2, definitions.Count - 1));
                if (YG2.saves.bakeryInventoryCounts != null)
                {
                    for (var i = 0; i < Mathf.Min(inventoryCounts.Length, YG2.saves.bakeryInventoryCounts.Length); i++)
                    {
                        inventoryCounts[i] = Mathf.Max(0, YG2.saves.bakeryInventoryCounts[i]);
                    }
                }

                if (YG2.saves.bakeryDiscoveredLevels != null)
                {
                    for (var i = 0; i < Mathf.Min(discoveredLevels.Length, YG2.saves.bakeryDiscoveredLevels.Length); i++)
                    {
                        discoveredLevels[i] = YG2.saves.bakeryDiscoveredLevels[i];
                    }
                }

                discoveredCount = 0;
                for (var i = 0; i < discoveredLevels.Length; i++)
                {
                    if (discoveredLevels[i])
                    {
                        discoveredCount++;
                    }
                }

                if (YG2.saves.bakeryItems != null)
                {
                    foreach (var savedItem in YG2.saves.bakeryItems)
                    {
                        if (savedItem == null)
                        {
                            continue;
                        }

                        var spawned = SpawnSweet(savedItem.level, new Vector2(savedItem.posX, savedItem.posY));
                        if (spawned != null)
                        {
                            spawned.transform.rotation = Quaternion.Euler(0f, 0f, savedItem.rotationZ);
                            spawned.Body.linearVelocity = Vector2.zero;
                            spawned.Body.angularVelocity = 0f;
                        }
                    }
                }
            }
            else
            {
                nextSpawnLevel = 0;
            }

            if (previewGuideRenderer != null)
            {
                previewGuideRenderer.enabled = true;
            }

            RefreshPreviewVisual();
        }
        finally
        {
            suppressSaveEvents = false;
        }
    }

    private void ClearBoard()
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] != null)
            {
                var item = items[i];
                item.PrepareForPooling();
                item.gameObject.SetActive(false);
                sweetPools[item.Level].Enqueue(item);
            }
        }

        items.Clear();
    }

    private void SubmitLeaderboard()
    {
        bestScore = Mathf.Max(bestScore, score);
        YG2.SetLeaderboard(ScoreLeaderboardName, bestScore);
    }

    private void ChooseNextSpawnLevel()
    {
        nextSpawnLevel = UnityEngine.Random.Range(0, 3);
        RefreshPreviewVisual();
    }

    private BakeryMergeItem SpawnSweet(int level, Vector2 position)
    {
        var definition = definitions[level];
        BakeryMergeItem item;
        if (sweetPools[level].Count > 0)
        {
            item = sweetPools[level].Dequeue();
            item.gameObject.name = definition.Name;
            item.transform.position = position;
            item.transform.rotation = Quaternion.identity;
            item.transform.localScale = Vector3.one * definition.Radius * 2f;
            item.gameObject.SetActive(true);
            item.ConfigureVisual(cakeSprites[level], cakesAtlasTexture != null ? Color.white : definition.MainColor, 10 + level);
            item.ResetPhysicsState();
            item.Initialize(this, level, item.Renderer, item.Body);
        }
        else
        {
            item = CreateSweet(level, position);
        }
        
        items.Add(item);
        DiscoverLevel(level);
        highestLevelReached = Mathf.Max(highestLevelReached, level);
        return item;
    }

    private BakeryMergeItem CreateSweet(int level, Vector2 position)
    {
        var definition = definitions[level];
        var sweet = new GameObject(definition.Name);
        sweet.transform.position = position;

        var renderer = sweet.AddComponent<SpriteRenderer>();
        renderer.sprite = cakeSprites[level];
        renderer.color = cakesAtlasTexture != null ? Color.white : definition.MainColor;
        renderer.sortingOrder = 10 + level;

        // Оптимизация: CircleCollider2D намного быстрее PolygonCollider2D
        var collider = sweet.AddComponent<CircleCollider2D>();
        collider.sharedMaterial = softPhysicsMaterial;

        var body = sweet.AddComponent<Rigidbody2D>();
        body.gravityScale = 1f;
        body.angularDamping = 1.4f;
        body.linearDamping = 0.08f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Оптимизация: Discrete режим для физики (намного легче для CPU)
        body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;

        sweet.transform.localScale = Vector3.one * definition.Radius * 2f;

        var item = sweet.AddComponent<BakeryMergeItem>();
        item.Initialize(this, level, renderer, body);

        if (cakesAtlasTexture == null)
        {
            BuildDecoration(sweet.transform, definition, level, renderer.sortingOrder, 1f, false);
        }

        return item;
    }

    private void BuildDecoration(Transform parent, SweetDefinition definition, int level, int sortingBase, float alpha, bool isPreview)
    {
        CreateVisualDisc(parent, "Shadow", new Vector3(0.08f, -0.08f, 0f), 0.98f, new Color(0.26f, 0.11f, 0.08f, 0.2f * alpha), sortingBase - 2);
        CreateVisualDisc(parent, "Gloss", new Vector3(-0.16f, 0.18f, 0f), 0.28f, new Color(1f, 1f, 1f, 0.3f * alpha), sortingBase + 1);

        switch (level)
        {
            case 0:
                CreateVisualDisc(parent, "Center", new Vector3(0f, -0.02f, 0f), 0.42f, WithAlpha(definition.AccentColor, alpha), sortingBase + 2);
                break;
            case 1:
                CreateCupRidges(parent, sortingBase + 2, alpha);
                CreateVisualDisc(parent, "Batter", new Vector3(0f, 0.03f, 0f), 0.58f, WithAlpha(definition.AccentColor, alpha), sortingBase + 3);
                break;
            case 2:
                CreateVisualDisc(parent, "BakedCore", Vector3.zero, 0.58f, WithAlpha(definition.AccentColor, alpha), sortingBase + 2);
                break;
            case 3:
                CreateLayerBands(parent, definition, sortingBase + 2, alpha);
                break;
            case 4:
                CreateLayerBands(parent, definition, sortingBase + 2, alpha);
                CreateVisualDisc(parent, "Berry", new Vector3(0.18f, 0.16f, 0f), 0.18f, WithAlpha(new Color(0.83f, 0.18f, 0.3f), alpha), sortingBase + 4);
                break;
            case 5:
                CreateVisualDisc(parent, "Hole", Vector3.zero, 0.36f, WithAlpha(new Color(0.99f, 0.94f, 0.92f), alpha), sortingBase + 4);
                CreateSprinkles(parent, definition, sortingBase + 5, alpha, 7);
                break;
            case 6:
                CreateCupWrapper(parent, sortingBase + 2, alpha);
                CreateVisualDisc(parent, "Cream", new Vector3(0f, 0.17f, 0f), 0.62f, WithAlpha(new Color(0.88f, 0.62f, 0.53f), alpha), sortingBase + 3);
                CreateVisualDisc(parent, "Cherry", new Vector3(0f, 0.42f, 0f), 0.18f, WithAlpha(definition.AccentColor, alpha), sortingBase + 4);
                break;
            case 7:
                CreateLayerBands(parent, definition, sortingBase + 2, alpha);
                CreateCandles(parent, sortingBase + 4, alpha, 3);
                break;
            case 8:
                CreateWeddingTiers(parent, definition, sortingBase + 2, alpha);
                CreateCandles(parent, sortingBase + 5, alpha, 4);
                break;
        }

        if (isPreview)
        {
            CreateVisualDisc(parent, "PreviewRing", Vector3.zero, 1.08f, new Color(1f, 1f, 1f, 0.08f), sortingBase - 1);
        }
    }

    internal void TryMerge(BakeryMergeItem first, BakeryMergeItem second)
    {
        if (first == null || second == null || first == second)
        {
            return;
        }

        if (first.Level != second.Level || first.IsBusy || second.IsBusy)
        {
            return;
        }

        if (first.Level >= definitions.Count - 1)
        {
            return;
        }

        StartCoroutine(MergeRoutine(first, second));
    }

    private IEnumerator MergeRoutine(BakeryMergeItem first, BakeryMergeItem second)
    {
        first.IsBusy = true;
        second.IsBusy = true;

        yield return new WaitForSeconds(0.04f);

        if (first == null || second == null)
        {
            yield break;
        }

        var nextLevel = first.Level + 1;
        var mergePosition = ((Vector2)first.transform.position + (Vector2)second.transform.position) * 0.5f;

        // Показываем эффект цвета теста (MainColor) уровня
        SpawnSugarBurst(mergePosition, definitions[nextLevel].MainColor);
        score += (nextLevel + 1) * 25;
        bestScore = Mathf.Max(bestScore, score);
        RegisterCraft(nextLevel);

        RemoveItem(first);
        RemoveItem(second);
        SpawnSweet(nextLevel, mergePosition);
        SaveProgressState(true);
    }

    private void SpawnSugarBurst(Vector2 position, Color color)
    {
        var particleSystem = sugarBurstPool.Count > 0 ? sugarBurstPool.Dequeue() : CreateSugarBurstSystem();
        var particlesObject = particleSystem.gameObject;
        particlesObject.transform.position = position;
        particlesObject.SetActive(true);

        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particleSystem.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        particleSystem.Play();
        StartCoroutine(ReturnSugarBurstToPool(particleSystem, 1.2f));
    }

    private ParticleSystem CreateSugarBurstSystem()
    {
        var particlesObject = new GameObject("SugarBurst");
        particlesObject.SetActive(false);

        var particleSystem = particlesObject.AddComponent<ParticleSystem>();
        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particleSystem.main;
        main.playOnAwake = false;
        main.duration = 0.55f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Color.white);
        main.gravityModifier = 0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;

        var emission = particleSystem.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

        var shape = particleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;

        var velocityOverLifetime = particleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocityOverLifetime.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocityOverLifetime.orbitalZ = new ParticleSystem.MinMaxCurve(-0.65f, 0.65f);

        // Настройка рендерера: убираем малиновые квадраты и задаем текстуру через материал
        var renderer = particlesObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var mat = new Material(Shader.Find("Sprites/Default"));
        if (circleSprite != null) mat.mainTexture = circleSprite.texture;
        renderer.material = mat;
        renderer.sortingOrder = 500; // Поверх тортиков

        return particleSystem;
    }

    private IEnumerator ReturnSugarBurstToPool(ParticleSystem particleSystem, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (particleSystem == null)
        {
            yield break;
        }

        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particleSystem.gameObject.SetActive(false);
        sugarBurstPool.Enqueue(particleSystem);
    }

    private void TriggerGameOver()
    {
        if (gameOver)
        {
            return;
        }

        gameOver = true;
        activeBooster = BoosterMode.None;

        if (previewRoot != null)
        {
            previewRoot.gameObject.SetActive(false);
        }

        if (previewGuideRenderer != null)
        {
            previewGuideRenderer.enabled = false;
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            item.Body.linearVelocity = Vector2.zero;
            item.Body.angularVelocity = 0f;
            item.Body.simulated = false;
        }

        SaveProgressState(true);
    }

    private void RestartGame()
    {
        // Показываем межстраничную рекламу соблюдая правила Яндекса: 
        // только после совершенного осмысленного действия пользователя (перезапуск игры после проигрыша).
        // SDK само обрабатывает паузу, если реклама загрузилась.
        YG2.InterstitialAdvShow();

        // Возвращаем все элементы в пулы через ClearBoard, затем уничтожаем содержимое пулов,
        // чтобы не было dangling references при полном перезапуске.
        ClearBoard();
        for (var i = 0; i < sweetPools.Length; i++)
        {
            while (sweetPools[i].Count > 0)
            {
                var pooled = sweetPools[i].Dequeue();
                if (pooled != null)
                {
                    Destroy(pooled.gameObject);
                }
            }
        }

        score = 0;
        coins = 0;
        overflowTimer = 0f;
        highestLevelReached = 0;
        discoveredCount = 0;
        activeBooster = BoosterMode.None;
        hoveredItem = null;
        gameOver = false;
        nextDropReadyTime = 0f;

        for (var i = 0; i < inventoryCounts.Length; i++)
        {
            inventoryCounts[i] = 0;
            discoveredLevels[i] = false;
        }

        if (previewGuideRenderer != null)
        {
            previewGuideRenderer.enabled = true;
        }

        ChooseNextSpawnLevel();
        RefreshPreviewVisual();
    }

    private void DrawMainMenu()
    {
        // 1. Full-screen dim overlay
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 2. Main Menu Panel
        var menuRect = new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.45f - 200f, 480f, 400f);
        DrawModernPanel(menuRect, new Color(0.24f, 0.14f, 0.1f, 0.98f)); // Richer dark chocolate
        // DrawTelegramButton(menuRect); // Removed

        GUILayout.BeginArea(menuRect);
    GUILayout.Space(24f); 
    
    // Large title in cream color
    var headerStyle = new GUIStyle(titleStyle) { fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = true };
    var ivoryColor = new Color(1f, 0.96f, 0.88f);
    headerStyle.normal.textColor = ivoryColor;
    headerStyle.hover.textColor = ivoryColor;
    headerStyle.active.textColor = ivoryColor;
    GUILayout.Label(Localize("Соедини и продавай сладости", "Merge and Sell Sweets"), headerStyle, GUILayout.Height(96f));
    
    GUILayout.Space(12f);
    
    var scoreStyle = new GUIStyle(centerStyle) { fontSize = 20 };
    var sandColor = new Color(0.95f, 0.88f, 0.75f);
    scoreStyle.normal.textColor = sandColor;
    scoreStyle.hover.textColor = sandColor;
    scoreStyle.active.textColor = sandColor;
    
    if (bestScore > 0)
    {
        GUILayout.Label($"{Localize("ЛУЧШИЙ СЧЁТ", "BEST SCORE")}: <color=#FFD700>{bestScore}</color>", scoreStyle);
    }
    else
    {
        GUILayout.Label(Localize("ГОТОВЫ НАЧАТЬ?", "READY TO START?"), scoreStyle);
    }

    GUILayout.FlexibleSpace();
    
    // Tasty "Dough-colored" Play Button
    GUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    GUI.backgroundColor = new Color(0.98f, 0.88f, 0.72f, 1f);
    var playBtnStyle = new GUIStyle(buttonStyle) { fontSize = 24, fontStyle = FontStyle.Bold };
    if (GUILayout.Button(Localize("ИГРАТЬ", "PLAY"), playBtnStyle, GUILayout.Height(64f), GUILayout.Width(280f)))
    {
        isGameStarted = true;
    }
    GUI.backgroundColor = Color.white;
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
    
    GUILayout.Space(12f);
    DrawLanguageSelector();
    
    GUILayout.Space(24f);
    GUILayout.EndArea();

        // Little version footer in cream
        var footerStyle = new GUIStyle(miniStyle);
        footerStyle.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height - 40f, 240f, 24f), "OVERHEATGAMES", footerStyle);
    }

    private void DrawLanguageSelector()
    {
        var captionStyle = new GUIStyle(miniStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        var tint = new Color(0.95f, 0.88f, 0.75f);
        captionStyle.normal.textColor = tint;
        captionStyle.hover.textColor = tint;
        captionStyle.active.textColor = tint;

        GUILayout.Label(Localize("Сообщество и Язык", "Community & Language"), captionStyle, GUILayout.Height(22f));
        GUILayout.Space(12f);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        // 1. Telegram Icon Button
        DrawTelegramIconButton();
        
        GUILayout.Space(18f);
        
        // 2. RU Flag
        DrawLanguageFlagButton("ru", russianFlagTexture, Localize("Русский", "Russian"));
        
        GUILayout.Space(18f);
        
        // 3. EN Flag
        DrawLanguageFlagButton("en", englishFlagTexture, Localize("Английский", "English"));
        
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawTelegramIconButton()
    {
        var btnRect = GUILayoutUtility.GetRect(78f, 54f, GUILayout.Width(78f), GUILayout.Height(54f));
        
        var oldColor = GUI.color;
        // Background (Same style as active/inactive flags but with Telegram Blue)
        GUI.color = new Color(0.15f, 0.63f, 0.9f, 0.95f);
        GUI.DrawTexture(btnRect, roundedBoxSprite.texture);
        GUI.color = oldColor;

        // Icon
        var iconSize = 34f;
        var iconRect = new Rect(btnRect.x + (btnRect.width - iconSize) * 0.5f, btnRect.y + (btnRect.height - iconSize) * 0.5f, iconSize, iconSize);
        if (telegramIconTexture != null)
        {
            GUI.DrawTexture(iconRect, telegramIconTexture, ScaleMode.ScaleToFit);
        }

        if (GUI.Button(btnRect, new GUIContent(string.Empty, "Telegram"), GUIStyle.none))
        {
            YG2.OnURL("https://t.me/OverHeatGames");
        }
    }

    private void DrawLanguageFlagButton(string languageCode, Texture2D flagTexture, string tooltip)
    {
        var flagRect = GUILayoutUtility.GetRect(78f, 54f, GUILayout.Width(78f), GUILayout.Height(54f));
        var isActive = string.Equals(YG2.lang, languageCode, StringComparison.OrdinalIgnoreCase);

        GUI.color = isActive ? new Color(1f, 0.92f, 0.78f, 1f) : new Color(0.46f, 0.28f, 0.18f, 0.95f);
        GUI.DrawTexture(flagRect, roundedBoxSprite.texture);
        GUI.color = Color.white;

        var innerRect = new Rect(flagRect.x + 7f, flagRect.y + 7f, flagRect.width - 14f, flagRect.height - 14f);
        if (flagTexture != null)
        {
            GUI.DrawTexture(innerRect, flagTexture, ScaleMode.StretchToFill, true);
        }

        if (GUI.Button(flagRect, new GUIContent(string.Empty, tooltip), GUIStyle.none))
        {
            YG2.SwitchLanguage(languageCode);
        }
    }

    private void RefreshPreviewVisual()
    {
        if (previewRoot != null)
        {
            Destroy(previewRoot.gameObject);
        }

        var definition = definitions[nextSpawnLevel];
        var previewObject = new GameObject("NextPreview");
        previewRoot = previewObject.transform;
        previewRoot.position = new Vector3(0f, SpawnY + 0.78f, 0f);
        previewRoot.localScale = Vector3.one * definition.Radius * 1.9f;

        var renderer = previewObject.AddComponent<SpriteRenderer>();
        renderer.sprite = cakeSprites[nextSpawnLevel];
        renderer.color = cakesAtlasTexture != null ? new Color(1f, 1f, 1f, 0.88f) : WithAlpha(definition.MainColor, 0.88f);
        renderer.sortingOrder = 120;

        if (cakesAtlasTexture == null)
        {
            BuildDecoration(previewRoot, definition, nextSpawnLevel, renderer.sortingOrder, 0.88f, true);
        }
    }



    private bool IsPointerOverUi(Vector2 guiPoint)
    {
        if (GetHeaderRect().Contains(guiPoint)) return true;
        if (GetDockRect().Contains(guiPoint)) return true;
        if (activeBooster != BoosterMode.None && GetBoosterHintRect().Contains(guiPoint)) return true;
        if (overflowTimer > 0f && GetWarningRect().Contains(guiPoint)) return true;

        if (isShopOpen)
        {
            if (GetShopRect().Contains(guiPoint)) return true;
        }

        return false;
    }

    private void DiscoverLevel(int level)
    {
        if (level >= 0 && level < discoveredLevels.Length && !discoveredLevels[level])
        {
            discoveredLevels[level] = true;
            discoveredCount++;
        }
    }

    private void RegisterCraft(int level)
    {
        DiscoverLevel(level);
        if (level >= 0 && level < inventoryCounts.Length)
        {
            inventoryCounts[level]++;
        }
    }

    private string TranslateBooster(BoosterMode mode)
    {
        return mode switch
        {
            BoosterMode.Remove => Localize("Лопатка", "Spatula"),
            BoosterMode.Upgrade => Localize("Мешок", "Piping Bag"),
            _ => mode.ToString()
        };
    }

    private string Localize(string russian, string english)
    {
        return IsRussianLanguage ? russian : english;
    }

    private void SellOne(int level)
    {
        if (inventoryCounts[level] <= 0)
        {
            return;
        }

        inventoryCounts[level]--;
        coins += definitions[level].SellPrice;
        SaveProgressState();
    }

    private void SellAll(int level)
    {
        if (inventoryCounts[level] <= 0)
        {
            return;
        }

        coins += inventoryCounts[level] * definitions[level].SellPrice;
        inventoryCounts[level] = 0;
        SaveProgressState();
    }

    private void EnsureGuiStyles()
    {
        if (titleStyle != null) return;

        // В WebGL CreateDynamicFontFromOSFont часто не имеет доступа к шрифтам
        // Поэтому лучше использовать либо встроенный скин, либо явно привязанный шрифт
        if (uiFont == null)
        {
            uiFont = customUiFont;
        }
        if (uiFont == null)
        {
            uiFont = LoadUiFont();
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            richText = true
        };
        if (uiFont != null) titleStyle.font = uiFont;
        
        titleStyle.normal.textColor = new Color(0.28f, 0.15f, 0.12f);
        titleStyle.hover.textColor = titleStyle.normal.textColor;

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            richText = true,
            alignment = TextAnchor.MiddleLeft
        };
        if (uiFont != null) bodyStyle.font = uiFont;
        
        bodyStyle.normal.textColor = new Color(0.35f, 0.2f, 0.18f);
        bodyStyle.hover.textColor = bodyStyle.normal.textColor;

        miniStyle = new GUIStyle(bodyStyle)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        if (uiFont != null) miniStyle.font = uiFont;
        miniStyle.normal.textColor = new Color(0.45f, 0.35f, 0.3f);
        miniStyle.hover.textColor = miniStyle.normal.textColor;

        warningStyle = new GUIStyle(bodyStyle)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        if (uiFont != null) warningStyle.font = uiFont;
        warningStyle.normal.textColor = new Color(0.85f, 0.15f, 0.1f);
        warningStyle.hover.textColor = warningStyle.normal.textColor;

        buttonStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(32, 32, 32, 32)
        };
        if (uiFont != null) buttonStyle.font = uiFont;
        // Use the white rounded box for modern flat buttons
        if (roundedBoxSprite != null)
        {
            buttonStyle.normal.background = roundedBoxSprite.texture;
            buttonStyle.hover.background = roundedBoxSprite.texture;
            buttonStyle.active.background = roundedBoxSprite.texture;
        }
        buttonStyle.normal.textColor = new Color(0.4f, 0.25f, 0.22f);
        buttonStyle.hover.textColor = new Color(0.2f, 0.1f, 0.08f);
        buttonStyle.active.textColor = new Color(0.6f, 0.4f, 0.35f);

        centerStyle = new GUIStyle(titleStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 28
        };
    }

    private static Font LoadUiFont()
    {
        var font = Resources.Load<Font>("ui_font");
        if (font != null)
        {
            return font;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null)
        {
            return font;
        }

        return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Tahoma", "Verdana" }, 16);
    }

    private void CreateCupRidges(Transform parent, int sortingBase, float alpha)
    {
        for (var i = 0; i < 8; i++)
        {
            var x = -0.32f + i * 0.09f;
            CreateVisualDisc(parent, $"Ridge_{i}", new Vector3(x, -0.12f, 0f), 0.12f, new Color(0.72f, 0.42f, 0.24f, 0.55f * alpha), sortingBase);
        }
    }

    private void CreateLayerBands(Transform parent, SweetDefinition definition, int sortingBase, float alpha)
    {
        CreateVisualBar(parent, "CreamBandTop", new Vector3(0f, 0.16f, 0f), new Vector3(0.9f, 0.16f, 1f), WithAlpha(definition.AccentColor, alpha), sortingBase);
        CreateVisualBar(parent, "CreamBandMid", new Vector3(0f, -0.02f, 0f), new Vector3(0.98f, 0.16f, 1f), new Color(1f, 0.98f, 0.96f, alpha), sortingBase + 1);
        CreateVisualBar(parent, "CreamBandBottom", new Vector3(0f, -0.2f, 0f), new Vector3(0.88f, 0.16f, 1f), WithAlpha(definition.AccentColor, alpha), sortingBase);
    }

    private void CreateCupWrapper(Transform parent, int sortingBase, float alpha)
    {
        CreateVisualBar(parent, "Wrapper", new Vector3(0f, -0.26f, 0f), new Vector3(0.72f, 0.36f, 1f), new Color(0.88f, 0.73f, 0.56f, alpha), sortingBase);
        for (var i = 0; i < 5; i++)
        {
            var x = -0.24f + i * 0.12f;
            CreateVisualBar(parent, $"WrapperStripe_{i}", new Vector3(x, -0.26f, 0f), new Vector3(0.05f, 0.32f, 1f), new Color(0.72f, 0.48f, 0.22f, 0.6f * alpha), sortingBase + 1);
        }
    }

    private void CreateCandles(Transform parent, int sortingBase, float alpha, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = count == 1 ? 0f : -0.22f + i * (0.44f / (count - 1));
            CreateVisualBar(parent, $"Candle_{i}", new Vector3(x, 0.36f, 0f), new Vector3(0.06f, 0.32f, 1f), new Color(0.96f, 0.84f, 0.46f, alpha), sortingBase);
            CreateVisualDisc(parent, $"Flame_{i}", new Vector3(x, 0.55f, 0f), 0.11f, new Color(1f, 0.62f, 0.23f, 0.85f * alpha), sortingBase + 1);
        }
    }

    private void CreateWeddingTiers(Transform parent, SweetDefinition definition, int sortingBase, float alpha)
    {
        CreateVisualDisc(parent, "TierBottom", new Vector3(0f, -0.18f, 0f), 0.88f, WithAlpha(definition.AccentColor, alpha), sortingBase);
        CreateVisualDisc(parent, "TierMid", new Vector3(0f, 0.08f, 0f), 0.62f, new Color(1f, 0.98f, 0.98f, alpha), sortingBase + 1);
        CreateVisualDisc(parent, "TierTop", new Vector3(0f, 0.34f, 0f), 0.38f, WithAlpha(definition.AccentColor, alpha), sortingBase + 2);
        CreateVisualDisc(parent, "Pearl", new Vector3(0f, 0.52f, 0f), 0.14f, new Color(0.91f, 0.79f, 0.42f, alpha), sortingBase + 3);
    }

    private void CreateSprinkles(Transform parent, SweetDefinition definition, int sortingBase, float alpha, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = i * Mathf.PI * 2f / count;
            var x = Mathf.Cos(angle) * 0.28f;
            var y = Mathf.Sin(angle) * 0.21f;
            CreateVisualDisc(parent, $"Sprinkle_{i}", new Vector3(x, y, 0f), 0.08f, WithAlpha(Color.Lerp(definition.AccentColor, Color.white, 0.42f), alpha), sortingBase);
        }
    }

    private void CreateVisualDisc(Transform parent, string name, Vector3 localPosition, float scale, Color color, int sortingOrder)
    {
        var disc = new GameObject(name);
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = localPosition;
        disc.transform.localScale = Vector3.one * scale;

        var renderer = disc.AddComponent<SpriteRenderer>();
        renderer.sprite = circleSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
    }

    private void CreateVisualBar(Transform parent, string name, Vector3 localPosition, Vector3 scale, Color color, int sortingOrder)
    {
        var bar = new GameObject(name);
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = localPosition;
        bar.transform.localScale = scale;

        var renderer = bar.AddComponent<SpriteRenderer>();
        renderer.sprite = squareSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
    }

    private void CreateCircleAccent(string name, Vector3 position, float scale, Color color)
    {
        var accent = new GameObject(name);
        accent.transform.position = position;
        accent.transform.localScale = Vector3.one * scale;

        var renderer = accent.AddComponent<SpriteRenderer>();
        renderer.sprite = circleSprite;
        renderer.color = color;
        renderer.sortingOrder = -1;
    }

    private void CreateWall(string name, Vector2 position, Vector2 size)
    {
        var wall = new GameObject(name);
        wall.transform.position = position;

        var collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;

        var renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = squareSprite;
        renderer.color = new Color(0.8f, 0.61f, 0.49f, 0.95f);
        renderer.sortingOrder = 2;
        wall.transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    private SpriteRenderer CreatePanel(string name, Vector3 position, Vector3 scale, Color color)
    {
        var panel = new GameObject(name);
        panel.transform.position = position;
        panel.transform.localScale = scale;

        var renderer = panel.AddComponent<SpriteRenderer>();
        renderer.sprite = squareSprite;
        renderer.color = color;
        renderer.sortingOrder = 0;
        return renderer;
    }

    private static Sprite CreateSquareSprite()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
    }

    private static Sprite CreateCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var center = (size - 1) * 0.5f;
        var radius = size * 0.5f;
        var pixels = new Color[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01((radius - distance) / 2.2f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Texture2D LoadAtlasTexture(string resourceName)
    {
        var resourceTexture = Resources.Load<Texture2D>(resourceName);
        if (resourceTexture != null)
        {
            return CloneTexture(resourceTexture);
        }

        return TryLoadAtlasTextureFromFile("Materials/torts.png");
    }

    private static Texture2D TryLoadAtlasTextureFromFile(string relativeAssetPath)
    {
        var fullPath = Path.Combine(Application.dataPath, relativeAssetPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(fullPath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        RemoveCheckerboardBackground(texture);
        texture.Apply();

        return texture;
    }

    private static Texture2D CloneTexture(Texture2D source)
    {
        var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        clone.filterMode = FilterMode.Bilinear;
        clone.wrapMode = TextureWrapMode.Clamp;

        try
        {
            clone.SetPixels32(source.GetPixels32());
        }
        catch (ArgumentException)
        {
            var previous = RenderTexture.active;
            var temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);

            try
            {
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;
                clone.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temp);
            }
        }

        RemoveCheckerboardBackground(clone);
        clone.Apply();
        return clone;
    }

    private static void RemoveCheckerboardBackground(Texture2D texture)
    {
        var width = texture.width;
        var height = texture.height;
        var pixels = texture.GetPixels32();
        var visited = new bool[pixels.Length];
        var queue = new Queue<int>(1024); // Начальная емкость для уменьшения аллокаций

        EnqueueBackgroundBorders(width, height, pixels, visited, queue);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            pixels[index].a = 0; // Прямая очистка альфы

            var x = index % width;
            var y = index / width;

            if (x > 0) TryVisit(x - 1, y, width, height, pixels, visited, queue);
            if (x < width - 1) TryVisit(x + 1, y, width, height, pixels, visited, queue);
            if (y > 0) TryVisit(x, y - 1, width, height, pixels, visited, queue);
            if (y < height - 1) TryVisit(x, y + 1, width, height, pixels, visited, queue);
        }

        texture.SetPixels32(pixels);
    }

    private static void EnqueueBackgroundBorders(int width, int height, Color32[] pixels, bool[] visited, Queue<int> queue)
    {
        for (var x = 0; x < width; x++)
        {
            TryVisit(x, 0, width, height, pixels, visited, queue);
            TryVisit(x, height - 1, width, height, pixels, visited, queue);
        }

        for (var y = 0; y < height; y++)
        {
            TryVisit(0, y, width, height, pixels, visited, queue);
            TryVisit(width - 1, y, width, height, pixels, visited, queue);
        }
    }

    private static void TryVisit(int x, int y, int width, int height, Color32[] pixels, bool[] visited, Queue<int> queue)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return;
        }

        var index = y * width + x;
        if (visited[index])
        {
            return;
        }

        visited[index] = true;
        if (!IsLikelyCheckerBackground(pixels[index]))
        {
            return;
        }

        queue.Enqueue(index);
    }

    private static bool IsLikelyCheckerBackground(Color32 pixel)
    {
        var max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
        var min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
        var isGray = max - min <= 12;
        var inCheckerRange = pixel.r >= 85 && pixel.r <= 140;
        return pixel.a > 0 && isGray && inCheckerRange;
    }

    private static Sprite CreateCleanCellSprite(Texture2D atlas, int x, int y, int width, int height)
    {
        var sourcePixels = atlas.GetPixels(x, y, width, height);
        var cellPixels = new Color32[sourcePixels.Length];
        for (var i = 0; i < sourcePixels.Length; i++)
        {
            cellPixels[i] = sourcePixels[i];
        }
        KeepLargestOpaqueIsland(cellPixels, width, height);

        if (!TryFindOpaqueBounds(cellPixels, width, height, out var minX, out var minY, out var maxX, out var maxY))
        {
            return Sprite.Create(atlas, new Rect(x, y, width, height), new Vector2(0.5f, 0.5f), width, 0, SpriteMeshType.Tight);
        }

        var trimmedWidth = maxX - minX + 1;
        var trimmedHeight = maxY - minY + 1;
        var trimmedTexture = new Texture2D(trimmedWidth, trimmedHeight, TextureFormat.RGBA32, false);
        trimmedTexture.filterMode = FilterMode.Bilinear;
        trimmedTexture.wrapMode = TextureWrapMode.Clamp;

        var trimmedPixels = new Color32[trimmedWidth * trimmedHeight];
        for (var iy = 0; iy < trimmedHeight; iy++)
        {
            for (var ix = 0; ix < trimmedWidth; ix++)
            {
                trimmedPixels[iy * trimmedWidth + ix] = cellPixels[(minY + iy) * width + (minX + ix)];
            }
        }

        trimmedTexture.SetPixels32(trimmedPixels);
        trimmedTexture.Apply();

        return Sprite.Create(
            trimmedTexture,
            new Rect(0f, 0f, trimmedWidth, trimmedHeight),
            new Vector2(0.5f, 0.5f),
            width,
            0,
            SpriteMeshType.Tight);
    }

    private static void KeepLargestOpaqueIsland(Color32[] pixels, int width, int height)
    {
        var visited = new bool[pixels.Length];
        var largestComponent = new List<int>();
        var queue = new Queue<int>();

        for (var i = 0; i < pixels.Length; i++)
        {
            if (visited[i] || pixels[i].a <= 16)
            {
                continue;
            }

            var component = new List<int>();
            visited[i] = true;
            queue.Enqueue(i);

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                component.Add(index);

                var x = index % width;
                var y = index / width;

                VisitOpaqueNeighbor(x - 1, y, width, height, pixels, visited, queue);
                VisitOpaqueNeighbor(x + 1, y, width, height, pixels, visited, queue);
                VisitOpaqueNeighbor(x, y - 1, width, height, pixels, visited, queue);
                VisitOpaqueNeighbor(x, y + 1, width, height, pixels, visited, queue);
            }

            if (component.Count > largestComponent.Count)
            {
                largestComponent = component;
            }
        }

        if (largestComponent.Count == 0)
        {
            return;
        }

        var keep = new bool[pixels.Length];
        foreach (var index in largestComponent)
        {
            keep[index] = true;
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            if (keep[i])
            {
                continue;
            }

            pixels[i].a = 0;
        }
    }

    private static void VisitOpaqueNeighbor(int x, int y, int width, int height, Color32[] pixels, bool[] visited, Queue<int> queue)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return;
        }

        var index = y * width + x;
        if (visited[index] || pixels[index].a <= 16)
        {
            return;
        }

        visited[index] = true;
        queue.Enqueue(index);
    }

    private static bool TryFindOpaqueBounds(Color32[] pixels, int width, int height, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= 16)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX >= minX && maxY >= minY;
    }

    private static Sprite CreateSphereSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var center = (size - 1) * 0.5f;
        var radius = size * 0.5f;
        var pixels = new Color[size * size];

        var lightDir = new Vector2(-0.35f, 0.45f).normalized;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01((radius - distance) / 2.2f);
                
                var normalizedDist = distance / radius;
                if (normalizedDist > 1f) normalizedDist = 1f;

                var z = Mathf.Sqrt(1f - normalizedDist * normalizedDist);
                var normal = new Vector3(dx / radius, dy / radius, z).normalized;
                var lightVec = new Vector3(lightDir.x, lightDir.y, 0.8f).normalized;

                var diffuse = Mathf.Max(0f, Vector3.Dot(normal, lightVec));
                var ambient = 0.55f;
                var lighting = ambient + diffuse * 0.55f;

                var edgeFade = 1f - Mathf.Pow(normalizedDist, 3.5f) * 0.35f;
                var intensity = lighting * edgeFade;

                Color pixelColor = new Color(intensity, intensity, intensity, alpha);
                
                // Cartoon outline (мультяшная обводка)
                if (normalizedDist > 0.91f)
                {
                    float outlineBlend = Mathf.Clamp01((normalizedDist - 0.91f) * 20f);
                    pixelColor = Color.Lerp(pixelColor, new Color(0.15f, 0.1f, 0.05f, alpha), outlineBlend);
                }

                pixels[y * size + x] = pixelColor;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a *= alpha;
        return color;
    }

    private static Sprite CreateRoundedBoxSprite(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(0, radius - x, x - (size - 1 - radius));
                var dy = Mathf.Max(0, radius - y, y - (size - 1 - radius));
                var dist = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01(radius + 0.5f - dist);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private static Texture2D CreateRussianFlagTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        for (var y = 0; y < height; y++)
        {
            var color = y < height / 3
                ? new Color(0.82f, 0.14f, 0.18f)
                : y < (height * 2) / 3
                    ? new Color(0.1f, 0.35f, 0.85f)
                    : Color.white;

            for (var x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D CreateUsFlagTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var red = new Color(0.7f, 0.13f, 0.2f);
        var white = Color.white;
        var blue = new Color(0.12f, 0.2f, 0.5f);
        var stripeHeight = Mathf.Max(1, Mathf.CeilToInt(height / 7f));

        for (var y = 0; y < height; y++)
        {
            var stripeColor = ((y / stripeHeight) % 2 == 0) ? red : white;
            for (var x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, stripeColor);
            }
        }

        var cantonWidth = Mathf.RoundToInt(width * 0.45f);
        var cantonHeight = Mathf.RoundToInt(height * 0.55f);
        for (var y = 0; y < cantonHeight; y++)
        {
            for (var x = 0; x < cantonWidth; x++)
            {
                texture.SetPixel(x, height - 1 - y, blue);
            }
        }

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                var px = Mathf.RoundToInt((col + 0.5f) * cantonWidth / 4f);
                var py = height - 1 - Mathf.RoundToInt((row + 0.5f) * cantonHeight / 3f);
                DrawFlagDot(texture, px, py, 2, white);
            }
        }

        texture.Apply();
        return texture;
    }

    private static void DrawFlagDot(Texture2D texture, int cx, int cy, int radius, Color color)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius)
                {
                    continue;
                }

                var px = cx + x;
                var py = cy + y;
                if (px < 0 || py < 0 || px >= texture.width || py >= texture.height)
                {
                    continue;
                }

                texture.SetPixel(px, py, color);
            }
        }
    }

    private readonly struct SweetDefinition
    {
        public SweetDefinition(BakeryMergeGame owner, string russianName, string englishName, Color mainColor, Color accentColor, float radius, int sellPrice)
        {
            Owner = owner;
            RussianName = russianName;
            EnglishName = englishName;
            MainColor = mainColor;
            AccentColor = accentColor;
            Radius = radius;
            SellPrice = sellPrice;
        }

        private BakeryMergeGame Owner { get; }
        private string RussianName { get; }
        private string EnglishName { get; }
        public string Name => Owner != null && Owner.IsRussianLanguage ? RussianName : EnglishName;
        public Color MainColor { get; }
        public Color AccentColor { get; }
        public float Radius { get; }
        public int SellPrice { get; }
    }

    private enum BoosterMode
    {
        None,
        Remove,
        Upgrade
    }

    private enum Digit
    {
        Digit1,
        Digit2,
        Digit3,
        Digit4
    }
}

public sealed class BakeryMergeItem : MonoBehaviour
{
    private BakeryMergeGame game;
    private SpriteRenderer spriteRenderer;
    private Color baseColor;

    public int Level { get; private set; }
    public bool IsBusy { get; set; }
    public Rigidbody2D Body { get; private set; }
    public SpriteRenderer Renderer => spriteRenderer;
    public float SpawnTime { get; private set; }

    public void Initialize(BakeryMergeGame bakeryMergeGame, int level, SpriteRenderer renderer, Rigidbody2D body)
    {
        game = bakeryMergeGame;
        Level = level;
        spriteRenderer = renderer;
        baseColor = renderer.color;
        Body = body;
        SpawnTime = Time.time;
        IsBusy = false;
        Body.simulated = true;
    }

    public void ConfigureVisual(Sprite sprite, Color color, int sortingOrder)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sprite = sprite;
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = sortingOrder;
        baseColor = color;
    }

    public void ResetPhysicsState()
    {
        if (Body == null)
        {
            return;
        }

        Body.simulated = true;
        Body.linearVelocity = Vector2.zero;
        Body.angularVelocity = 0f;
        Body.rotation = 0f;
        Body.position = transform.position;
    }

    public void PrepareForPooling()
    {
        IsBusy = false;
        if (Body != null)
        {
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.simulated = false;
        }

        SetHighlight(false);
    }

    public void SetHighlight(bool isHighlighted)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.color = isHighlighted ? Color.Lerp(baseColor, Color.white, 0.28f) : baseColor;
    }

    public bool IsOverflowThreat(float warningY)
    {
        if (transform.position.y <= warningY)
        {
            return false;
        }

        if (Time.time - SpawnTime < 0.9f)
        {
            return false;
        }

        return Body.linearVelocity.sqrMagnitude < 0.2f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var other = collision.collider.GetComponent<BakeryMergeItem>();
        if (other == null)
        {
            return;
        }

        game.TryMerge(this, other);
    }
}
