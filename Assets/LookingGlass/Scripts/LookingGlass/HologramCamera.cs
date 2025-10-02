//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LookingGlass {
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [HelpURL("https://docs.lookingglassfactory.com/Unity/Scripts/HologramCamera/")]
    [DefaultExecutionOrder(DefaultExecutionOrder)]
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public partial class HologramCamera : MonoBehaviour {
        private static List<HologramCamera> all = new List<HologramCamera>(8);

        /// <summary>
        /// Called when <see cref="All"/> is updated due to OnEnable or OnDisable on any <see cref="HologramCamera"/> component.
        /// </summary>
        public static event Action onListChanged;

        internal static event Action<HologramCamera> onAnyRenderSettingsChanged;
        internal static event Action<HologramCamera> onAnyCalibrationReloaded;

        /// <summary>
        /// <para>
        /// Determines whether or not errors will be silenced.<br />
        /// These errors may log depending on the graphics API your project is built with, due to Unity's <see cref="Display"/> API's limited support non-DirectX-based graphics APIs.
        /// </para>
        /// <para>Note that in order for setting this to take effect, you must set this value before any <see cref="HologramCamera"/> initializes during its Awake.</para>
        /// <para>See also: <seealso cref="DefaultExecutionOrder"/>, <seealso cref="RuntimeInitializeOnLoadMethodAttribute"/>, <seealso cref="RuntimeInitializeLoadType.BeforeSceneLoad"/></para>
        /// </summary>
        /// <example>
        /// <code>
        /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        /// private static void SupressErrorsExample() {
        ///     HologramCamera.SuppressDisplayWindowingErrors = true;
        /// }
        /// </code>
        /// </example>
        public static bool SuppressDisplayWindowingErrors { get; set; } = false;

        /// <summary>
        /// The most-recently enabled <see cref="HologramCamera"/> component, or <c>null</c> if there is none.
        /// </summary>
        public static HologramCamera Instance {
            get {
                if (all == null || all.Count <= 0)
                    return null;
                return all[all.Count - 1];
            }
        }

        public static int Count => all.Count;
        public static bool AnyEnabled => all.Count > 0;
        internal static Func<HologramCamera, bool> UsePostProcessing { get; set; }

        public static HologramCamera Get(int index) => all[index];
        public static IEnumerable<HologramCamera> All {
            get {
                foreach (HologramCamera h in all)
                    yield return h;
            }
        }

        public static void UpdateAllCalibrations() {
            foreach (HologramCamera h in all)
                h.UpdateCalibration();
        }

        private static void RegisterToList(HologramCamera hologramCamera) {
            Assert.IsNotNull(hologramCamera);
            all.Add(hologramCamera);
            onListChanged?.Invoke();
        }

        private static void UnregisterFromList(HologramCamera hologramCamera) {
            Assert.IsNotNull(hologramCamera);
            all.Remove(hologramCamera);
            onListChanged?.Invoke();
        }

        #region Versions
        internal static bool isDevVersion = false;

        private static PluginVersionAsset versionAsset;

        public static bool IsVersionLoaded {
            get {
                try {
                    //We expect that sometimes (such as during serialization callbacks), we won't be able to load
                    //the version via the Resources API.
                    EnsureAssetIsLoaded();
                } catch { }
                return versionAsset != null;
            }
        }

        /// <summary>
        /// The currently-running version of the LookingGlass Unity Plugin.
        /// </summary>
        public static SemanticVersion Version {
            get {
                EnsureAssetIsLoaded();
                return versionAsset.Version;
            }
        }

#if UNITY_EDITOR
        static HologramCamera() {
            //NOTE: Cannot load from Resources in static constructor
            EditorApplication.update += AutoLoadVersion;
        }
#endif


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoadVersion() {
#if UNITY_EDITOR
            EditorApplication.update -= AutoLoadVersion;
#endif
            EnsureAssetIsLoaded();
        }

        internal static readonly Version MajorHDRPRefactorVersion = new Version(1, 5, 0);
        internal static bool IsUpdatingBetween(Version previous, Version next, Version threshold)
            => previous < next && previous < threshold && next >= threshold;

        private static void EnsureAssetIsLoaded() {
            if (versionAsset == null)
                versionAsset = Resources.Load<PluginVersionAsset>("Plugin Version");
        }

        #endregion

        public const int DefaultExecutionOrder = -1000;
        private const string SingleViewCameraName = "Single-View Camera";
        private const string PostProcessCameraName = "Post-Process Camera";
        private const string FinalScreenCameraName = "Final Screen Camera";

        private static readonly HologramRenderSettings DefaultCustomRenderSettings = HologramRenderSettings.Get(DeviceType.Portrait);

        [SerializeField, HideInInspector] private SerializableVersion lastSavedVersion;


        [Tooltip("In builds, this forces the main window to snap to the LKG display, despite being associated with your primary display (Display index 0).\n\n" +
            "Set this to false for 2-monitor setups that require screen-based raycasting (including clicking in the UI) to match to the correct screen. Otherwise, leave this set to true.\n\n" +
            "Has no effect in the Unity editor.\n\n" +
            "The default value is true.\n"
        )]
        [SerializeField] internal bool forceDisplayIndex = true;

        [Tooltip("The Unity display that the LKG device associated with this component will render to.")]
        [SerializeField] internal DisplayTarget targetDisplay;

        //NOTE: Data duplication below for lkgName and lkgIndex, because we need to save (serialize) them! (Because the calibration data is NOT serialized)
        [Tooltip("The name of the Looking Glass (LKG) device that this component is connected with.\n\n" +
            "A " + nameof(Camera) + " component is only connected to 1 device at a time.")]
        [FormerlySerializedAs("targetLKGName")]
        [SerializeField] internal string targetLKG;

        [Tooltip("The type of device that is being emulated right now, since there are no LKG devices plugged in or recognized.")]
        [SerializeField] internal DeviceType emulatedDevice = DeviceType.Portrait;

        [SerializeField] private bool preview2D = false;
        [Tooltip("Does this camera show a hologram preview in the inspector and scene view?\n" +
            "Note that this only has an effect in the Unity editor, not in builds.")]
        [SerializeField] private bool showHologramPreview = false;

        [Tooltip("The realtime quilt rendered by this Holoplay capture.")]
        [SerializeField] internal RenderTexture quiltTexture;
        [SerializeField] private bool useQuiltAsset;
        [SerializeField] internal QuiltPreset quiltPreset = QuiltPreset.Automatic;
        [SerializeField] internal HologramRenderSettings customRenderSettings = HologramRenderSettings.Get(DeviceType.Portrait);

        [Tooltip("Defines a sequence of rendering commands that can be mixed together in the form of quilt textures.\n\n" +
            "This can be useful, for example, if you wish to mix 2D cameras, pre-rendered quilts, or quilt videos with the LookingGlass capture's realtime renderer.")]
        [SerializeField] internal RenderStack renderStack = new RenderStack();


        [FormerlySerializedAs("cameraData")]
        [SerializeField, HideInInspector] private HologramCameraProperties cameraProperties = new HologramCameraProperties();
        [SerializeField, HideInInspector] private HologramCameraGizmos gizmos = new HologramCameraGizmos();
        [SerializeField, HideInInspector] private HologramCameraEvents events = new HologramCameraEvents();
        [SerializeField, HideInInspector] private OptimizationProperties optimization = new OptimizationProperties();
        [SerializeField, HideInInspector] private HologramCameraDebugging debugging = new HologramCameraDebugging();


        //NOTE: Duplicate logic with QuiltCapture.initialized
        /// <summary>
        /// Allows us to initialize immediately during Awake,
        /// and re-initialize on every subsequence OnEnable call after being disabled and re-enabled.
        /// </summary>
        private bool initializationStarted = false;
        private bool initialized = false;
        private TaskCompletionSource<bool> initializationTcs = new();

        private Camera singleViewCamera;
        private Camera postProcessCamera;
        private Camera finalScreenCamera;
        private
#if UNITY_EDITOR
            new
#endif
            MultiViewRenderer renderer;

        [NonSerialized] private bool hadPreview2D = false; //Used for detecting changes in the editor
        [NonSerialized] private bool wasSavingQuilt;

        [NonSerialized] private Material lightfieldMaterial;
        [NonSerialized] private bool foundCalibration;
        [NonSerialized] public Calibration cal;

        //TODO: HoPS infrastructure improvements to avoid us from needing to subvert the
        //calibration provided by HoPS in this UnityPlugin! (See also: Calibration.cs)
        private Calibration unmodifiedCalibration;

        private bool isUsingCustomRenderResolution;
        private int customXPos;
        private int customYPos;
        private int customRenderWidth;
        private int customRenderHeight;

        private bool frameRendered;
        private bool frameRendered2DPreview;
        private bool debugInfo;

        private RenderTextureFormat quiltTextureOriginalFormatUsed;
        private RenderTexture preview2DRT;
        private RenderTexture singleViewRT;
        internal List<Func<bool>> renderBlackIfAny = new();
        private QuiltCapture currentCapture;

        private List<Component> hideFlagsObjects = new List<Component>();

        private RenderTexture depthQuiltTexture;
        internal bool clearDirtyFlag = false;

        public event Action onTargetDisplayChanged;
        public event Action onQuiltChanged;
        public event Action onRenderSettingsChanged;
        public event Action onCalibrationChanged;

        public bool Preview2D {
            get { return preview2D; }
            set {
                hadPreview2D = preview2D = value;

                //If we need anything to change immediately when setting Preview2D, we can do that here
            }
        }

        public bool ShowHologramPreview {
            get { return showHologramPreview; }
            set {
                showHologramPreview = value;
#if UNITY_EDITOR
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
#endif
            }
        }

        public RenderStack RenderStack => renderStack;

        public HologramCameraProperties CameraProperties => cameraProperties;
        public HologramCameraGizmos Gizmos => gizmos;
        public HologramCameraEvents Events => events;
        public OptimizationProperties Optimization => optimization;
        public HologramCameraDebugging Debugging => debugging;
        public IEnumerable<PropertyGroup> PropertyGroups {
            get {
                yield return cameraProperties;
                yield return gizmos;
                yield return events;
                yield return optimization;
                yield return debugging;
            }
        }

        public bool ForceDisplayIndex {
            get { return forceDisplayIndex; }
        }

        public DisplayTarget TargetDisplay {
            get { return targetDisplay; }
            set {
                targetDisplay = value;
                if (finalScreenCamera != null)
                    finalScreenCamera.targetDisplay = (int) targetDisplay;
                onTargetDisplayChanged?.Invoke();
            }
        }

        public bool HasTargetDevice => !string.IsNullOrWhiteSpace(targetLKG);

        public string TargetLKG {
            get { return targetLKG; }
            set {
                targetLKG = value;
                UpdateCalibration();
            }
        }

        public DeviceType EmulatedDevice {
            get { return emulatedDevice; }
            set {
                emulatedDevice = value;
                ValidateCanChangeRenderSettings();

                cameraProperties.NearClipFactor = DeviceSettings.Get(emulatedDevice).nearClip;
                quiltPreset = QuiltPreset.Automatic;
                UpdateCalibration();
                OnRenderSettingsChanged();
            }
        }

        public QuiltPreset QuiltPreset {
            get { return quiltPreset; }
            set {
                ValidateCanChangeRenderSettings();
                quiltPreset = value;
                SetupQuilt();
                OnRenderSettingsChanged();
            }
        }

        public HologramRenderSettings RenderSettings {
            get {
                HologramRenderSettings result = GetRenderSettingsInternal();
                if (NeedsQuiltResetup(result))
                    SetupQuilt(result);
                return result;
            }
        }

        public HologramRenderSettings CustomRenderSettings {
            get { return customRenderSettings; }
            internal set {
                ValidateCanChangeRenderSettings();
                customRenderSettings = value;
                SetupQuilt();
                OnRenderSettingsChanged();
            }
        }

        public int ScreenWidth => foundCalibration ? cal.screenWidth : DeviceSettings.Get(emulatedDevice).screenWidth;
        public int ScreenHeight => foundCalibration ? cal.screenHeight : DeviceSettings.Get(emulatedDevice).screenHeight;

        /// <summary>
        /// <para>The final aspect ratio value used for rendering. This value is always greater than zero.</para>
        /// <para>Returns <see cref="RenderSettings"/>'s aspect value if it is greater than zero, or the <see cref="UnmodifiedCalibration"/>'s aspect value otherwise.</para>
        /// </summary>
        public float Aspect {
            get {
                float result = GetRenderSettingsInternal().aspect;

                //NOTE: expected that the quilt aspect might be -1, meaning: default to calibration aspect
                if (result <= 0)
                    result = UnmodifiedCalibration.GetAspect();

                Assert.IsTrue(result > 0, "The final aspect value used for rendering should be greater than zero!");
                return result;
            }
        }

        public string DeviceTypeName => foundCalibration ? DeviceSettings.GetName(cal) : DeviceSettings.Get(emulatedDevice).name;

        public RenderTexture QuiltTexture {
            get {
                if (isActiveAndEnabled) {
                    if (NeedsQuiltResetup())
                        SetupQuilt();
                    Assert.IsNotNull(quiltTexture);
                } else {
                    Debug.Log(quiltTexture);
                    Assert.IsNull(quiltTexture);
                }

                return quiltTexture;
            }
        }

        /// <summary>
        /// The material with the lightfield shader, used in the final graphics blit to the screen.
        /// It accepts the quilt texture as its main texture.
        /// </summary>
        public Material LightfieldMaterial {
            get {
                if (lightfieldMaterial == null)
                    CreateLightfieldMaterial();
                return lightfieldMaterial;
            }
        }

        public bool UseQuiltAsset {
            get { return useQuiltAsset; }
            set {
                wasSavingQuilt = useQuiltAsset = value;
#if UNITY_EDITOR
                SetupQuilt();
                if (useQuiltAsset)
                    SaveOrUseQuiltAsset();
#endif
            }
        }

        public bool Initialized {
            get { return initialized; }
            set {
                initialized = value;
                if (initialized) {
                    initializationTcs.TrySetResult(true);
                } else {
                    initializationTcs = new();
                }
            }
        }
        public Task WaitForInitialization() => initializationTcs.Task;

        //How the cameras work:
        //1. The finalScreenCamera begins rendering automatically, since it is enabled.
        //2. The singleViewCamera renders into RenderTextures,
        //        either for rendering the quilt, or the 2D preview.
        //3. Then, the postProcessCamera is set to render no Meshes, and discards its own RenderTexture source.
        //        INSTEAD, it takes a RenderTexture (quiltRT) from LookingGlass.cs and blits it with the lightfield shader back into the RenderTexture.
        //4. Finally, the finalScreenCamera blits the result ONTO THE SCREEN.(A camera required for that), since its targetTexture is always null.

        /// <summary>
        /// <para>Renders individual views of the scene, where each view may be composited into the <see cref="LookingGlass"/> quilt.</para>
        /// <para>When in 2D preview mode, only 1 view is rendered directly to the screen.</para>
        /// <para>This camera is not directly used for rendering to the screen. The results of its renders are used as intermediate steps in the rendering process.</para>
        /// </summary>
        public Camera SingleViewCamera => singleViewCamera;

        /// <summary>
        /// <para>The <see cref="Camera"/> used apply final post-processing to a single view of the scene, or a quilt of the scene.</para>
        /// <para>This camera is not directly used for rendering to the screen. It is only used for applying graphical changes in internal <see cref="RenderTexture"/>s.</para>
        /// </summary>
        public Camera PostProcessCamera => postProcessCamera;

        /// <summary>
        /// The camera used for blitting the final <see cref="RenderTexture"/> to the screen.<br />
        /// In Unity, the easiest and best-supported way to do this is by using a Camera directly.
        /// </summary>
        internal Camera FinalScreenCamera => finalScreenCamera;

        internal MultiViewRenderer Renderer => renderer;

        public RenderTexture Preview2DRT {
            get {
                if (preview2DRT == null || !frameRendered2DPreview)
                    RenderPreview2D();
                return preview2DRT;
            }
        }

        public Calibration Calibration {
            get { return cal; }
        }

        public Calibration UnmodifiedCalibration => unmodifiedCalibration;

        /// <summary>
        /// Defines whether or not the <see cref="Calibration"/> is temporarily modified to fit
        /// a width and height that's different from the target LKG device's native resolution.<br />
        /// When this is true, the <see cref="Calibration"/>'s <see cref="Calibration.screenWidth"/> and <see cref="Calibration.screenHeight"/>
        /// will be modified, as well as any other calibration fields that depend on those two fields.<br /><br />
        ///
        /// Currently, this is only an internal, editor-only feature used to render the preview window in the Unity editor, due to the following:
        /// <list type="bullet">
        /// <item>The preview's editor window title bar can't easily and consistently be hidden across Windows, MacOS, and Linux.</item>
        /// <item>The OS task bar may prevent the window from becoming full-screen native resolution on the LKG display.</item>
        /// </list>
        /// <para>See also: <seealso cref="Calibration"/>, <seealso cref="UnmodifiedCalibration"/></para>
        /// </summary>
        public bool IsUsingCustomResolution {
            get {
                if (!Application.isEditor)
                    Assert.IsFalse(isUsingCustomRenderResolution, "The custom resolution feature has not yet been intended for or tested to work in builds!");
                return isUsingCustomRenderResolution;
            }
        }

        /// <summary>
        /// <para>
        /// Determines whether or not a solid black color is rendered.
        /// This overrides all rendering, and is used for better UX in the Unity editor (to avoid seeing inaccurate frames while Unity GUI is setting up).
        /// </para>
        /// <para>This always return <c>false</c> when <see cref="AreRenderSettingsLockedForRecording"/> is <c>true</c>, so that quilt recordings do not accidentally contain black frames.</para>
        /// </summary>
        internal bool RenderBlack {
            get {
                if (AreRenderSettingsLockedForRecording)
                    return false;
                foreach (Func<bool> possibility in renderBlackIfAny)
                    if (possibility != null && possibility())
                        return true;
                return false;
            }
        }

        internal RenderTexture DepthQuiltTexture {
            get { return depthQuiltTexture; }
            set { depthQuiltTexture = value; }
        }

        public bool AreRenderSettingsLockedForRecording => currentCapture != null;

        public bool IsSameDevice(HologramCamera other) {
            if (other == null)
                return false;
            if (!foundCalibration || !other.foundCalibration)
                return false;
            return cal.IsSameDevice(other.cal);
        }

        #region Unity Messages
        internal void OnValidate() {
#if UNITY_EDITOR
            //NOTE: We delay this because changed events may be called, causing re-renders, which are not allowed during OnValidate
            EditorApplication.delayCall += () => {
                //NOTE: If entering/exiting playmode, make sure we skip the delayed OnValidate if our MonoBehaviour object no longer exists from before
                if (this == null)
                    return;

                if (preview2D != hadPreview2D)
                    Preview2D = preview2D;
                if (useQuiltAsset != wasSavingQuilt)
                    UseQuiltAsset = useQuiltAsset;
            };
#endif
        }

        //WARNING: When clicking on the HologramCamera prefab, it does NOT get Awake/OnEnable calls to initialize itself!
        private void Awake() {
            initializationStarted = true;
            Initialize();
        }

        private void OnEnable() {
            if (!initializationStarted) {
                initializationStarted = true;
                Initialize();
            }
        }

        private void OnDisable() {
            initializationStarted = false;
            Initialized = false;
            debugging.onShowAllObjectsChanged -= SetAllObjectHideFlags;
#if UNITY_EDITOR
            customRenderSettings.onAspectChanged -= OnCustomRenderSettingsChanged;
#endif

            UnregisterFromList(this);

            if (lightfieldMaterial != null)
                DestroyImmediate(lightfieldMaterial);
            if (quiltTexture != null)
                quiltTexture.Release();
            if (preview2DRT != null)
                preview2DRT.Release();

            //NOTE: We don't destroy the post-process camera because the PostProcessLayer component requires it stays on
            if (singleViewCamera != null)
                DestroyImmediate(singleViewCamera.gameObject);
            if (finalScreenCamera != null)
                DestroyImmediate(finalScreenCamera.gameObject);

            if (depthQuiltTexture != null)
                depthQuiltTexture.Release();

            singleViewCamera = finalScreenCamera = null;
        }

        private void Update() {
            finalScreenCamera.clearFlags = cameraProperties.ClearFlags;
            finalScreenCamera.backgroundColor = finalScreenCamera.clearFlags == CameraClearFlags.Depth ? Color.clear : cameraProperties.BackgroundColor;

            frameRendered = false;
            frameRendered2DPreview = false;

            bool shiftF8 = false;
            bool esc = false;

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = InputSystem.GetDevice<Keyboard>();
            if (keyboard != null) {
                if (keyboard.shiftKey.isPressed && keyboard.f8Key.wasPressedThisFrame)
                    shiftF8 = true;
                if (keyboard.escapeKey.wasPressedThisFrame)
                    esc = true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.F8))
                shiftF8 = true;
            if (Input.GetKeyDown(KeyCode.Escape))
                esc = true;
#endif

            if (shiftF8)
                debugInfo = !debugInfo;
            if (esc)
                debugInfo = false;
        }

        private void LateUpdate() {
            if (!frameRendered)
                PrepareFieldsBeforeRendering();
        }

        private void OnGUI() {
            if (debugInfo) {
                Color previousColor = GUI.color;

                // start drawing stuff
                int unitDiv = 20;
                int unit = Mathf.Min(Screen.width, Screen.height) / unitDiv;
                Rect rect = new Rect(unit, unit, unit * (unitDiv - 2), unit * (unitDiv - 2));

                GUI.color = Color.black;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                rect = new Rect(unit * 2, unit * 2, unit * (unitDiv - 4), unit * (unitDiv - 4));

                GUILayout.BeginArea(rect);
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = unit;
                GUI.color = new Color(0.5f, 0.8f, 0.5f, 1);

                GUILayout.Label("LookingGlass SDK " + Version.Value, labelStyle);
                GUILayout.Space(unit);
                GUI.color = foundCalibration ? new Color(0.5f, 1, 0.5f) : new Color(1, 0.5f, 0.5f);
                GUILayout.Label("calibration: " + (foundCalibration ? "loaded" : "not found"), labelStyle);

                //TODO: This is giving a false positive currently
                //GUILayout.Space(unit);
                //GUI.color = new Color(0.5f, 0.5f, 0.5f, 1);
                //GUILayout.Label("lkg display: " + (loadResults.lkgDisplayFound ? "found" : "not found"), labelStyle);

                GUILayout.EndArea();

                GUI.color = previousColor;
            }
        }

        private void OnDrawGizmos() {
            gizmos.DrawGizmos(this);
        }
        #endregion

        private void Initialize() {
            try {
                InitSections();
                RegisterToList(this);

                if (lightfieldMaterial == null)
                    CreateLightfieldMaterial();

                SetupAllCameras();
                SetupRenderer();
                Preview2D = preview2D;

                UpdateCalibration();

                _ = SetupScreenResolutionAfterHTTPCalibration();
                SetupQuilt();
                PrepareFieldsBeforeRendering();

#if UNITY_EDITOR
                MatchCustomRenderingResolutionForDevice();
#endif

                debugging.onShowAllObjectsChanged -= SetAllObjectHideFlags;
                debugging.onShowAllObjectsChanged += SetAllObjectHideFlags;
#if UNITY_EDITOR
                customRenderSettings.onAspectChanged -= OnCustomRenderSettingsChanged;
                customRenderSettings.onAspectChanged += OnCustomRenderSettingsChanged;
#endif

                Initialized = true;
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        private async Task SetupScreenResolutionAfterHTTPCalibration() {
            if (CalibrationSystem.IsLoadingCalibrations)
                await CalibrationSystem.WaitForCalibrations();
            SetupScreenResolution();
        }

        private void SetupScreenResolution() {
            if (!Application.isEditor) {
                //Unity Bug Report IN-18776
                //  - https://unity3d.atlassian.net/servicedesk/customer/portal/2/IN-18776
                //  - https://issuetracker.unity3d.com/product/unity/issues/guid/UUM-21974
                //Display.SetParams(...) causes crashes on OpenGLCore.
                //According to Unity, "Multi-display is only supported for DirectX graphics APIs".
                GraphicsDeviceType graphicsAPI = SystemInfo.graphicsDeviceType;

                if (graphicsAPI == GraphicsDeviceType.OpenGLCore) {
                    if (!SuppressDisplayWindowingErrors)
                        Debug.LogError("Failed to target your Looking Glass display!\n" +
                            "Multi-display applications is only supported for DirectX graphics APIs in Unity.\n" +
                            "Display.SetParams(...) crashes in OpenGLCore. (see the following for more information: https://issuetracker.unity3d.com/product/unity/issues/guid/UUM-21974).\n");
                } else {
                    //NOTE: This is REQUIRED for using Display.SetParams(...)!
                    //See Unity docs on this at: https://docs.unity3d.com/ScriptReference/Display.SetParams.html

                    //NOTE: WITHOUT this line, subsequent calls to Display.displays[0].SetParams(...) HAVE NO EFFECT!
                    Display.displays[0].Activate();
#if UNITY_STANDALONE_WIN
                    if (ForceDisplayIndex)
                        Display.displays[0].SetParams(cal.screenWidth, cal.screenHeight, cal.xpos, cal.ypos);
#elif UNITY_STANDALONE_OSX
                    StartCoroutine(MacWindowing.SetupMacWindowing(cal));
#endif
                }
            }

#if !UNITY_STANDALONE_OSX
            //This sets up the window to play on the looking glass,
            //NOTE: This must be executed after display reposition
            //YAY! This FIXED the issue with cal.screenHeight or 0 as the SetParams height making the window only go about half way down the screen!
            //This also lets the lenticular shader render properly!
            Screen.SetResolution(cal.screenWidth, cal.screenHeight, true);
#endif
        }

        private void SetupRenderer() {
            MultiViewRenderer.Next = this;
            renderer = finalScreenCamera.gameObject.AddComponent<MultiViewRenderer>();
            renderStack.RenderToQuilt(this);
        }

        internal void InitSections() {
            foreach (PropertyGroup group in PropertyGroups)
                group.Init(this);
        }

#if UNITY_EDITOR
        private void OnCustomRenderSettingsChanged() {
            //NOTE: Cannot update many things off the main thread, which is why we're delay-calling on the main thread:
            if (quiltPreset == QuiltPreset.Custom)
                EditorApplication.delayCall += OnRenderSettingsChanged;
        }
#endif

        private void OnRenderSettingsChanged() {
            onAnyRenderSettingsChanged?.Invoke(this);
            onRenderSettingsChanged?.Invoke();
#if UNITY_EDITOR
            //NOTE: Delaying because Unity was complaining about recursive GUI calls upon stopping recording
            EditorApplication.delayCall += () => GameViewExtensions.RepaintAllViewsImmediately();
#endif
        }

        private void ValidateCanChangeRenderSettings() {
            if (AreRenderSettingsLockedForRecording)
                throw new InvalidOperationException("You cannot set quilt settings during recording! Please use " + nameof(QuiltCapture) + "'s override settings instead.");
        }

        internal static HologramCamera GetLastForDevice(string serial) {
            HologramCamera last = null;
            foreach (HologramCamera other in All)
                if (last == null || (other != last && other.IsSameDevice(last) && other.cameraProperties.Depth > last.cameraProperties.Depth))
                    last = other;
            return last;
        }

        internal bool IsLastForDevice() {
            foreach (HologramCamera other in All)
                if (other != this && other.IsSameDevice(this) && other.cameraProperties.Depth > cameraProperties.Depth)
                    return false;
            return true;
        }

        //NOTE: Use this when we want to get the currently-used quilt settings, but NOT call the logic to regenerate the quilt texture and such!
        private HologramRenderSettings GetRenderSettingsInternal() =>
            (quiltPreset == QuiltPreset.Custom) ? customRenderSettings : HologramRenderSettings.Get(quiltPreset, unmodifiedCalibration);

        public void UseCustomRenderSettings(HologramRenderSettings settings) {
            SetQuiltPresetAndSettings(QuiltPreset.Custom, settings);
        }

        public void UseAutomaticRenderSettings() {
            SetQuiltPresetAndSettings(QuiltPreset.Automatic, HologramRenderSettings.Get(DeviceType.Portrait));
        }

        public void SetQuiltPresetAndSettings(QuiltPreset quiltPreset, HologramRenderSettings customRenderSettings) {
            ValidateCanChangeRenderSettings();
            this.quiltPreset = quiltPreset;
            this.customRenderSettings = customRenderSettings;
            SetupQuilt();
            OnRenderSettingsChanged();
        }

        private void CreateLightfieldMaterial() {
            lightfieldMaterial = new Material(Util.FindShader("LookingGlass/Lightfield"));
        }

        private void UpdateFinalCameraDepth() {
            if (finalScreenCamera != null)
                finalScreenCamera.depth = cameraProperties.Depth;
        }

        //NOTE: Only the finalScreenCamera is set with enabled = true, because it's the only camera here meant to write to the screen.
        //Thus, its targetTexture is null, and it's enabled to call OnRenderImage(...) and write each frame to the screen.
        //These other cameras are just for rendering intermediate results.
        private void SetupAllCameras() {
            singleViewCamera = new GameObject(SingleViewCameraName).AddComponent<Camera>();
            singleViewCamera.transform.SetParent(transform, false);
            singleViewCamera.enabled = false;
            singleViewCamera.stereoTargetEye = StereoTargetEyeMask.None; //NOTE: This is needed for better XR support
            SetHideFlagsOnObject(singleViewCamera);

            if (UsePostProcessing?.Invoke(this) ?? false) {
                //NOTE: The post-processing camera (if we use one) should be on the LookingGlass GameObject,
                //so that a PostProcessLayer (or other post-processing components) put on the LookingGlass object itself will have an effect!
                //(Better UX for the user for integrating post-processing with LookingGlass components)
                if (!TryGetComponent(out postProcessCamera))
                    postProcessCamera = gameObject.AddComponent<Camera>();
                postProcessCamera.enabled = false;
                postProcessCamera.stereoTargetEye = StereoTargetEyeMask.None;
                SetHideFlagsOnObject(postProcessCamera);
            } else {
                if (TryGetComponent(out Camera c))
                    SetHideFlagsOnObject(c);
            }

            finalScreenCamera = new GameObject(FinalScreenCameraName).AddComponent<Camera>();
            finalScreenCamera.transform.SetParent(transform, false);

#if UNITY_2017_3_OR_NEWER
            finalScreenCamera.allowDynamicResolution = false;
#endif
            finalScreenCamera.allowHDR = false;
            finalScreenCamera.allowMSAA = false;
            finalScreenCamera.cullingMask = 0;
            finalScreenCamera.clearFlags = CameraClearFlags.Nothing;
            finalScreenCamera.targetDisplay = (int) targetDisplay;
            finalScreenCamera.stereoTargetEye = StereoTargetEyeMask.None;
            SetHideFlagsOnObject(finalScreenCamera);
        }


        private void SetAllObjectHideFlags() {
            for (int i = hideFlagsObjects.Count - 1; i >= 0; i--) {
                if (hideFlagsObjects[i] == null) {
                    hideFlagsObjects.RemoveAt(i);
                    continue;
                }
                SetHideFlagsOnObject(hideFlagsObjects[i]);
            }
        }

        /// <summary>
        /// <para>Sets the hide flags on a temporary object used by this <see cref="Camera"/> script.</para>
        /// <para>If the <paramref name="tempComponent"/> is on the same <see cref="GameObject"/> as this script, it sets the component's hide flags.<br />
        /// When <paramref name="tempComponent"/> on a different game object from this <see cref="Camera"/> script, <paramref name="tempComponent"/>'s game object's hide flags are set instead.</para>
        /// </summary>
        internal HideFlags SetHideFlagsOnObject(Component tempComponent, bool skipRegistration = false) {
            HideFlags hideFlags = HideFlags.None;
            if (tempComponent == null)
                return hideFlags;

            bool isOnCurrentGameObject = tempComponent.gameObject == gameObject;
            bool hide = !debugging.ShowAllObjects;

            if (isOnCurrentGameObject && tempComponent is Camera) {
                //We WANT to save a Camera component if it's on the same LookingGlass game object,
                //So that PostProcessLayers don't complain with warning logs about adding a Camera before us...
            } else {
                hideFlags |= HideFlags.DontSave;
            }

            //NOTE: HideInHierarchy on a Component allows us to hide that specific Component's gizmos!
            if (hide)
                hideFlags |= HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            if (isOnCurrentGameObject)
                tempComponent.hideFlags = hideFlags;
            else
                tempComponent.gameObject.hideFlags = hideFlags;

            if (!skipRegistration && !hideFlagsObjects.Contains(tempComponent))
                hideFlagsObjects.Add(tempComponent);

            return hideFlags;
        }

        internal void ResetCameras() {
            UpdateFinalCameraDepth();

            if (singleViewCamera != null) {
                cameraProperties.SetCamera(singleViewCamera);
                //TODO: [CRT-3174] Look into whether or not we should set the snigleViewCamera's clear flags to nothing or not..
                //      And we should investigate into how to fix the depthiness slider's Skybox bug..
                //This was our previous logic:
                // switch (cameraProperties.ClearFlags) {
                //     case CameraClearFlags.Depth:
                //     case CameraClearFlags.SolidColor:
                //     case CameraClearFlags.Nothing:
                //         //IMPORTANT: The single-view camera MUST clear after each render, or else there will be
                //         //ghosting of previous single-view renders left in the next quilt views to render (getting more and more worse as more views are rendered)

                //         //HOWEVER, it should be left on Color.clear for the background color, because the quilt is already cleared with
                //         //our background color before any of the single-views are copied into it.
                //         singleViewCamera.clearFlags = CameraClearFlags.SolidColor;
                //         singleViewCamera.backgroundColor = Color.clear;
                //         break;
                // }

                switch (cameraProperties.ClearFlags) {
                    case CameraClearFlags.Depth:
                    case CameraClearFlags.SolidColor:
                        //NOTE: We already clear the single - view textures during our render loop in MultiViewRendering, so we instruct Unity's camera to not do any clearing itself.
                        singleViewCamera.clearFlags = CameraClearFlags.Nothing;
                        break;
                }

                singleViewCamera.ResetWorldToCameraMatrix();
                singleViewCamera.ResetProjectionMatrix();
                Matrix4x4 centerViewMatrix = singleViewCamera.worldToCameraMatrix;
                Matrix4x4 centerProjMatrix = singleViewCamera.projectionMatrix;

                if (cameraProperties.TransformMode == TransformMode.Volume)
                    centerViewMatrix.m23 -= focalPlane;

                if (cameraProperties.UseFrustumTarget) {
                    Vector3 targetPos = -cameraProperties.FrustumTarget.localPosition;
                    centerViewMatrix.m03 += targetPos.x;
                    centerProjMatrix.m02 += targetPos.x / (size * Aspect);
                    centerViewMatrix.m13 += targetPos.y;
                    centerProjMatrix.m12 += targetPos.y / size;
                } else {
                    if (cameraProperties.HorizontalFrustumOffset != 0) {
                        float offset = focalPlane * Mathf.Tan(Mathf.Deg2Rad * cameraProperties.HorizontalFrustumOffset);
                        centerViewMatrix.m03 += offset;
                        centerProjMatrix.m02 += offset / (size * Aspect);
                    }
                    if (cameraProperties.VerticalFrustumOffset != 0) {
                        float offset = focalPlane * Mathf.Tan(Mathf.Deg2Rad * cameraProperties.VerticalFrustumOffset);
                        centerViewMatrix.m13 += offset;
                        centerProjMatrix.m12 += offset / size;
                    }
                }

                singleViewCamera.worldToCameraMatrix = centerViewMatrix;
                singleViewCamera.projectionMatrix = centerProjMatrix;
            }
        }

        public void UpdateLightfieldMaterial() => MultiViewRendering.SetLightfieldMaterialSettings(this, LightfieldMaterial);

        //NOTE: Custom rendering resolution is only needed for the editor preview window for now, NOT in builds.
#if UNITY_EDITOR
        private void MatchCustomRenderingResolutionForDevice() {
            foreach (HologramCamera other in All) {
                if (other != this && other.IsSameDevice(this) && other.IsUsingCustomResolution) {
                    UseCustomRenderingResolutionInternal(other.customXPos, other.customYPos, other.customRenderWidth, other.customRenderHeight);
                    return;
                }
            }
            ClearCustomRenderingResolutionInternal();
        }

        internal void UseCustomRenderingResolution(int xpos, int ypos, int width, int height) {
            UseCustomRenderingResolutionInternal(xpos, ypos, width, height);
            foreach (HologramCamera other in All) {
                if (other != this && other.IsSameDevice(this))
                    other.UseCustomRenderingResolutionInternal(xpos, ypos, width, height);
            }
        }

        internal void ClearCustomRenderingResolution() {
            ClearCustomRenderingResolutionInternal();
            foreach (HologramCamera other in All)
                if (other != this && other.IsSameDevice(this))
                    other.ClearCustomRenderingResolutionInternal();
        }

        private void UseCustomRenderingResolutionInternal(int xpos, int ypos, int width, int height) {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, nameof(width) + " must be greater than zero! (" + xpos + ", " + ypos + ", " + width + ", " + height + ")");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, nameof(height) + " must be greater than zero! (" + xpos + ", " + ypos + ", " + width + ", " + height + ")");

            isUsingCustomRenderResolution = true;
            customXPos = xpos;
            customYPos = ypos;
            customRenderWidth = width;
            customRenderHeight = height;
            cal = cal.CopyWithCustomResolution(customXPos, customYPos, customRenderWidth, customRenderHeight);
        }

        private void ClearCustomRenderingResolutionInternal() {
            isUsingCustomRenderResolution = false;
            customXPos = 0;
            customYPos = 0;
            customRenderWidth = 0;
            customRenderHeight = 0;
            cal = unmodifiedCalibration;
        }
#endif

        internal bool MigrateLKGNameToSerial() {
            foreach (Calibration c in CalibrationSystem.Calibrations) {
                if (targetLKG == c.LKGname) {
                    cal = c;
                    foundCalibration = true;
                    targetLKG = c.serial;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Note: This does NOT call <see cref="CalibrationSystem.ReloadCalibrations"/>. This just reads the currently-available calibration data.
        /// </summary>
        /// <returns></returns>
        public void UpdateCalibration() {
            try {
                HologramRenderSettings previousRenderSettings = GetRenderSettingsInternal();
                Calibration cal = default;
                bool setDirty = false;
                string prevTargetName = targetLKG;
                foundCalibration = false;

                bool manualCalibrationInEffect = Debugging.ManualCalibrationMode != ManualCalibrationMode.None;
                if (manualCalibrationInEffect) {
                    switch (Debugging.ManualCalibrationMode) {
                        case ManualCalibrationMode.UseCalibrationFile:
                            TextAsset file = Debugging.CalibrationFile;
                            if (file != null) {
                                if (ToolkitAPI.Device.Calibration.TryParse(file.text, out ToolkitAPI.Device.Calibration toolkitCal)) {
                                    cal = new Calibration(toolkitCal);
                                    foundCalibration = true;
                                }
                            }
                            break;
                        case ManualCalibrationMode.UseManualSettings:
                            cal = Debugging.ManualCalibration;
                            foundCalibration = true;
                            break;
                    }
                } 
                
                if (!foundCalibration) {
                    foreach (Calibration c in CalibrationSystem.Calibrations) {
                        if (targetLKG == c.serial) {
                            cal = c;
                            foundCalibration = true;
                            break;
                        }
                    }
                    //MIGRATION: This is to help users who are upgrading to UnityPlugin v2.0.0-alpha15 or newer.
                    //      Before v2.0.0-alpha15, we used to store targetLKGName, which was the LKGname (hwid) of the LKG device.
                    //      Now, v2.0.0-alpha15+ uses the Calibration.serial field to check if 2 Calibrations represent the same LKG device.
                    if (!foundCalibration) {
                        if (MigrateLKGNameToSerial()) {
                            setDirty = true;
                            foundCalibration = true;
                        }
                    }

                    if (!foundCalibration) {
                        if (CalibrationSystem.CalibrationCount > 0) {
                            cal = CalibrationSystem.Get(0);
                            setDirty = true;
                            foundCalibration = true;
                        }
                    }

                    if (!foundCalibration) {
                        DeviceSettings emulatedSettings = DeviceSettings.Get(emulatedDevice);
                        cal = new Calibration(ScreenWidth, ScreenHeight);

                        //TODO: Clean up "nice-looking" default values for emulating devices here
                        cal.serial = emulatedSettings.name;
                        cal.aspect = emulatedSettings.nativeAspect;

                        cal.center = 0.5f;
                        cal.dpi = 200;

                        switch (emulatedDevice) {
                            case DeviceType.Portrait:
                            case DeviceType._16in:
                                cal.viewCone = 58;
                                break;
                            case DeviceType._32in:
                            case DeviceType._65in:
                                cal.viewCone = 53;
                                break;
                        }
                    }
                }

                Assert.IsTrue(cal.GetType().IsValueType, "The copy below assumes that "
                    + nameof(LookingGlass.Calibration) + " is a value type (struct), so the single equals operator creates a deep copy!");

                unmodifiedCalibration = cal;
                if (isUsingCustomRenderResolution)
                    this.cal = cal = unmodifiedCalibration.CopyWithCustomResolution(customXPos, customYPos, customRenderWidth, customRenderHeight);
                else
                    this.cal = cal;

                targetLKG = this.cal.serial;

#if UNITY_EDITOR
                if (setDirty && !Application.IsPlaying(this)) {
                    //NOTE: EditorSceneManager.MarkSceneDirty(...) was mysteriously returning false, maybe cause this is called during Awake()?
                    //      So just wait 1 editor frame:
                    EditorApplication.delayCall += () => {
                        Scene scene = gameObject.scene;
                        string updateMessage = "Updating " + name + " to use Calibration.serial instead of Calibration.LKGname (" + prevTargetName + " -> " + cal.serial + "), since the LKG UnityPlugin is now v2.0.0-alpha15+.";
                        if (scene.IsValid()) {
                            updateMessage += "\nMarking scene as dirty: " + scene.name;
                            EditorSceneManager.MarkSceneDirty(scene);
                        } else {
                            //REVIEW: Check this works:
                            string prefabPath = AssetDatabase.GetAssetPath(this);
                            updateMessage += "\nMarking prefab as dirty: " + prefabPath;
                            EditorUtility.SetDirty(gameObject);
                        }
                        Debug.LogWarning(updateMessage);
                    };
                }
#endif

                //We need to set up the quilt again because quilt settings may be changed.
                if (!previousRenderSettings.Equals(GetRenderSettingsInternal()))
                    SetupQuilt();

                UpdateLightfieldMaterial();
                onAnyCalibrationReloaded?.Invoke(this);
                onCalibrationChanged?.Invoke();
            } catch (Exception e) {
                Debug.LogError("Error occurred during " + (Application.isPlaying ? "Playmode" : "Editmode"));
                Debug.LogException(e);
                foundCalibration = false;
            }
        }

        internal void LockRenderSettingsForRecording(QuiltCapture capture) {
            Assert.IsNotNull(capture);
            Assert.IsNull(currentCapture);
            currentCapture = capture;
        }

        internal void UnlockRenderSettingsFromRecording(QuiltCapture capture) {
            Assert.AreEqual(currentCapture, capture);
            currentCapture = null;
        }

        private RenderTextureFormat GetQuiltFormat() {
            if (allowHDR)
                return RenderTextureFormat.DefaultHDR;
            return RenderTextureFormat.Default;
        }

        private bool NeedsQuiltResetup() => NeedsQuiltResetup(RenderSettings);
        private bool NeedsQuiltResetup(HologramRenderSettings renderSettings) {
            if (!isActiveAndEnabled)
                return false;

            RenderTexture quilt = quiltTexture;
            if (quilt == null)
                return true;

            if (quilt.width != renderSettings.quiltWidth || quilt.height != renderSettings.quiltHeight)
                return true;

            //WARNING: It'd be dangerous to compare the quilt.format, because RenderTexture.Default and RenderTexture.DefaultHDR evaluate to something that's NOT equal to themselves,
            //so this would constantly say "YES! The quilt needs to be re-setup every frame!" and allocate your system out of RAM/VRAM!
            if (/*quilt.format*/ quiltTextureOriginalFormatUsed != GetQuiltFormat())
                return true;
            return false;
        }

        public RenderTexture SetupQuilt() => SetupQuilt(RenderSettings);

        /// <summary>
        /// <para>Sets up the quilt and the quilt <see cref="RenderTexture"/>.</para>
        /// <para>This should be called after modifying custom quilt settings.</para>
        /// </summary>
        public RenderTexture SetupQuilt(HologramRenderSettings renderSettings) {
            Assert.IsTrue(renderSettings.quiltWidth > 0, nameof(HologramRenderSettings.quiltWidth) + " must be greater than zero! (" + renderSettings.quiltWidth + " was given instead!)");
            Assert.IsTrue(renderSettings.quiltHeight > 0, nameof(HologramRenderSettings.quiltHeight) + " must be greater than zero! (" + renderSettings.quiltHeight + " was given instead!)");

            customRenderSettings.Setup(); // even if not custom quilt, just set this up anyway
            RenderTexture quilt = quiltTexture;
            if (quilt != null) {
                if (quilt == RenderTexture.active)
                    RenderTexture.active = null;
                quilt.Release();
            }

            quiltTextureOriginalFormatUsed = GetQuiltFormat();
            quilt = new RenderTexture(renderSettings.quiltWidth, renderSettings.quiltHeight, 0, quiltTextureOriginalFormatUsed) {
                filterMode = FilterMode.Point,
                hideFlags = (useQuiltAsset) ? HideFlags.None : HideFlags.DontSave
            };

            quilt.name = "LookingGlass Quilt";
            quilt.enableRandomWrite = true;
            quilt.Create();
            quiltTexture = quilt;

#if UNITY_EDITOR
            if (useQuiltAsset)
                SaveOrUseQuiltAsset();
#endif

            UpdateLightfieldMaterial();

            //Pass some stuff globally for post-processing
            Shader.SetGlobalVector("hp_quiltViewSize", new Vector4(
                (float) renderSettings.ViewWidth / renderSettings.quiltWidth,
                (float) renderSettings.ViewHeight / renderSettings.quiltHeight,
                renderSettings.ViewWidth,
                renderSettings.ViewHeight
            ));
            onQuiltChanged?.Invoke();
            return quilt;
        }

#if UNITY_EDITOR
        public void SaveOrUseQuiltAsset() {
            quiltTexture.hideFlags &= ~HideFlags.DontSave;
            var existingPath = AssetDatabase.GetAssetPath(quiltTexture);
            string quiltPath = "Assets/quilt_tex.renderTexture";
            if (File.Exists(quiltPath)) {
                quiltTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(quiltPath);
            } else if (existingPath == null || existingPath == "") {
                AssetDatabase.CreateAsset(quiltTexture, quiltPath);
                AssetDatabase.Refresh();
                EditorApplication.RepaintProjectWindow();
            } else {
                quiltTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(existingPath);
            }
        }
#endif

        private void ClearTexturesAfterClearFlagsChanged() {
            MultiViewRendering.Clear(QuiltTexture, CameraClearFlags.SolidColor, Color.clear);
            MultiViewRendering.Clear(Preview2DRT, CameraClearFlags.SolidColor, Color.clear);
        }

        private void PrepareFieldsBeforeRendering() {
            ResetCameras();
            if (Initialized)
                UpdateLightfieldMaterial();
            cameraProperties.UpdateAutomaticFields();
        }

        public void RenderQuilt(bool forceRender = false, bool ignorePostProcessing = false) {
            RenderQuiltLayer(forceRender, ignorePostProcessing);
            renderStack.RenderToQuilt(this);
        }

        public void RenderQuiltLayer(bool forceRender = false, bool ignorePostProcessing = false) {
            if (!forceRender && frameRendered)
                return;
            frameRendered = true;
            PrepareFieldsBeforeRendering();

            if (clearDirtyFlag) {
                clearDirtyFlag = false;
                ClearTexturesAfterClearFlagsChanged();
            }
            MultiViewRendering.ClearBeforeRendering(quiltTexture, this);

            if (RenderBlack) {
                Graphics.Blit(Util.OpaqueBlackTexture, quiltTexture);
                return;
            }

            UpdateFinalCameraDepth();

            MultiViewRendering.RenderQuilt(this, ignorePostProcessing, (int viewIndex) => {
                events.OnViewRendered?.Invoke(this, viewIndex);
            });
        }

        public RenderTexture RenderPreview2D(bool forceRender = false, bool ignorePostProcessing = false) {
            if (!forceRender && frameRendered2DPreview)
                return preview2DRT;
            frameRendered2DPreview = true;
            PrepareFieldsBeforeRendering();

            if (clearDirtyFlag) {
                clearDirtyFlag = false;
                ClearTexturesAfterClearFlagsChanged();
            }

            if (RenderBlack) {
                //TODO: Create a method similar to SetupQuilt(...) but for the Preview2D texture..
                RenderTexture t = Preview2DRT;
                if (t != null) {
                    Graphics.Blit(Util.OpaqueBlackTexture, t);
                    return t;
                }
            }

            RenderTexture next = MultiViewRendering.RenderPreview2D(this, ignorePostProcessing);
            if (next != preview2DRT) {
                if (preview2DRT != null) {
                    if (Application.IsPlaying(gameObject))
                        Destroy(preview2DRT);
                    else
                        DestroyImmediate(preview2DRT);
                }
                preview2DRT = next;
            }
            return preview2DRT;
        }
    }
}
