using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityInjector;
using UnityInjector.Attributes;
using static UnityEngine.GUILayout;


namespace COM3D2.InOutAnimation.Plugin
{
    [PluginFilter(PluginFilter)]
    [PluginName(PluginName)]
    [PluginVersion(PluginVersion)]
    public class InOutAnimation : PluginBase
    {
        private const string PluginFilter = "com3d2x64",
            PluginName = "InOutAnimation",
            PluginVersion = "1.0.0.1";

        private const string PathConfig = @"IOAnim",
            FileNameConfig = @"Settings";

        private const string ConfigPanel = "ConfigPanel";
        private const string ResultPanel = "ResultPanel";

        private const string NameHideScrollField = "current_panel_display_",
            NameSkillSel = "SkillSelectViewer",
            NameParamView = "ParameterViewer";

        private const string HiddenShader = "Hidden/Internal-Colored";
        private const string NoZTestShader = "CM3D2/Toony_Lighted_Trans_NoZTest";
        private const string SE_CumShot = "SE016";

        private interface IInitializable
        {
            void Initialize();
        }

        #region Variables

        private readonly Version pluginGameVer = new Version("1.32.0");
        private Version currentGameVer;

        private bool isStudioMode;
        private Mediator mediator;
        private AnmScript script;
        private State current;
        private FlipAnim[] flipAnims;
        private PokoCam pokoCam;
        private FaceCam faceCam;
        private Controller controller;
        private static Settings settings;
        private int windowId;

        private IEnumerable<AudioSource> _playingSE;
        private HideScroll _hideScroll;
        private GameObject configPanel;
        private GameObject resultPanel;

        private WfScreenChildren screenChildren;

        private readonly BindingFlags bindingFlags =
            BindingFlags.GetField | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly | BindingFlags.Instance;

        private IInitializable[] _initializables;

        private MessageBox _msgbox;
        private static bool _debug;

        #endregion

        #region MonoBehavior

        private void Awake()
        {
            DontDestroyOnLoad(this);
        }

        private void Start()
        {
            currentGameVer = new Version(GameUty.GetBuildVersionText());
            if (pluginGameVer > currentGameVer)
            {
                enabled = false;
                return;
            }

            Initialize();

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                foreach (var flipAnim in flipAnims)
                    flipAnim.Load();

                settings = Settings.Load();
                isStudioMode = scene.name.Contains("ScenePhotoMode");
            };

            SceneManager.sceneUnloaded += scene =>
            {
                AllReset();
                controller.showController = false;
                Settings.Save();
            };
        }

        private void Update()
        {
            if (isStudioMode)
                return;

            if (Input.anyKey)
                CheckInput();

            if (!settings.enablePlugin)
                return;

            mediator.FindTarget(current);

            script.Parse(mediator);

            if (!mediator.TargetMaid.IsValid() || !current.isPlay)
                return;

            mediator.PrepareMuffs();

            mediator.PreparePokos(current);

            if (mediator.muffsPrepared && mediator.pokosPrepared)
            {
                mediator.ValidatePair(current);

                mediator.UpdateValues(current);

                switch (Time.frameCount % 30)
                {
                    case 0:
                        current.showSkillSelect = IsShowHideScrolls(NameSkillSel);
                        break;

                    case 10:
                        current.showParameter = IsShowHideScrolls(NameParamView);
                        break;

                    case 15:
                        current.showResultPanel = IsShowResultPanel();
                        break;

                    case 20:
                        current.showConfigPanel = IsShowConfigPanel();
                        break;
                }

                if (mediator.manLength > 0)
                    current.isShooting = IsShooting();

                current.CanDrawOverlay = true;
            }

            if (Time.frameCount % 20 == 5)
                CheckScreenFade();

            if (!settings.enablePlugin || !mediator.TargetMan.IsValid()) return;
            var smr = mediator.TargetMan.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s =>
                s.name.Equals("karada", StringComparison.InvariantCultureIgnoreCase));
            if (smr == null)
                return;
            var mat = smr.materials[1];
            if (origShader == null)
                origShader = mat.shader;

            if (!current.isPlay || mat.shader.name == "CM3D2/Man" && !settings.OnGUI_HidePenis)
            {
                mat.shader = origShader;
            }
            else if (mat.shader.name == origShader.name && settings.OnGUI_HidePenis)
            {
                mat.shader = manShader;
                mat.SetFloat("_FloatValue2", 0f);
                mat.renderQueue = 3500;
            }
        }

        private readonly Shader manShader = Shader.Find("CM3D2/Man");
        private Shader origShader;


        private void LateUpdate()
        {
            if (current.CanDrawOverlay)
                DrawOverlay();
            current.CanDrawOverlay = false;


            if (!_debug)
                return;

            if (_msgbox == null)
            {
                _msgbox = new MessageBox();
                FindObjectOfType<SystemInfoHUD>().enabled = true;
                return;
            }

            foreach (var poko in mediator.pokos)
                poko?.DrawTrails();
        }

        private void OnGUI()
        {
            if (isStudioMode)
                return;

            if (controller.showController)
                controller.Draw();

            if (settings.enablePlugin && mediator.TargetMaid.IsValid() && current.isPlay)
            {
                DrawFlipAnims();
                DrawPokoCam();
                DrawFaceCam();
            }

            if (_debug)
                _msgbox?.Draw(
                    new GUIContent(
                        $"maid:{script.MaidAnmName}\n" +
                        $"man0:{script.ManAnmName}\n" +
                        $"currentSkill:{current.CurrentSkill}\n" +
                        $"prim:{current.PrimaryMuff.ToString()}\n" +
                        $"mode:{current.PlayMode.ToString()}\n" +
                        $"num:{current.MuffNum.ToString()}\n" +
                        $"stat:{current.PlayState}\n" +
                        $"play:{current.isPlay} onani:{current.isOnani}\n" +
                        $"pokotype:{current.PokoType} \n" +
                        $"poko[0]:{mediator.pokos[0]?.TargetMuff}\n" +
                        $"poko[1]:{mediator.pokos[1]?.TargetMuff}\n" +
                        $"poko[2]:{mediator.pokos[2]?.TargetMuff}\n" +
                        $"skillSel:{current.showSkillSelect} " +
                        $"paramVew:{current.showParameter} " +
                        $"confPanel:{current.showConfigPanel}" +
                        $"resPanel:{current.showResultPanel}" +
                        $"isShot:{current.isShooting}"
                    )
                );
        }

        #endregion

        #region Methods

        private void Initialize()
        {
            mediator = new Mediator();
            current = new State();
            script = new AnmScript(current);
            pokoCam = new PokoCam();
            faceCam = new FaceCam();
            windowId = PluginName.ToCharArray().Sum(x => x) * PluginVersion.ToCharArray().Sum(x => x);

            var dirInfoInjector = new DirectoryInfo($@"{Assembly.GetExecutingAssembly().Location}").Parent;
            var dirInfoConfig = new DirectoryInfo($@"{dirInfoInjector}\Config\{PathConfig}");
            settings = Settings.Load(new FileInfo($@"{dirInfoConfig}\{FileNameConfig}"));

            var pathTexSrcFront = new DirectoryInfo($@"{dirInfoConfig}\v");
            var pathTexSrcBack = new DirectoryInfo($@"{dirInfoConfig}\a");
            var pathTexSrcMouth = new DirectoryInfo($@"{dirInfoConfig}\o");

            flipAnims = new[]
            {
                new FlipAnim(pathTexSrcFront),
                new FlipAnim(pathTexSrcBack),
                new FlipAnim(pathTexSrcMouth)
            };

            _initializables = new IInitializable[]
            {
                mediator, current, script, pokoCam, faceCam, flipAnims[0], flipAnims[1], flipAnims[2]
            };

            controller = new Controller(this);
        }

        private void DrawFlipAnims()
        {
            for (var i = 0; i < 3; i++)
            {
                if ((current.PokoType == PokoType.Finger || current.PokoType == PokoType.Tongue) && i < 2)
                    continue;
                flipAnims[i].Draw(i, current);
            }
        }

        private void DrawPokoCam()
        {
            if (settings.enablePokoCam && mediator.manLength > 0)
            {
                if (!pokoCam.SetupCam(mediator.TargetMaid, mediator.TargetMan, current.PrimaryMuff)) return;
                var n = (int) current.PrimaryMuff;
                if (n > 2) n = 2;
                if (flipAnims[n] == null && mediator.muffs[n] == null) return;
                pokoCam.Action();
                //var rect = settings.PokoCam_CustomPos && settings.PokoCam_Pos.size != Vector2.zero
                //    ? settings.PokoCam_Pos
                //    : settings.PokoCam_Pos = flipAnims[n].CalcRect(3, current.showParameter);
                var rect = FlipAnim.ScreenRect.Get(3, current.showParameter);
                pokoCam.Draw(current, rect);
            }
            else
            {
                pokoCam.Initialize();
            }
        }

        private void DrawFaceCam()
        {
            if (!settings.enableFaceCam || !faceCam.SetupCam(mediator.TargetMaid)) return;
            faceCam.Action();
            faceCam.Draw(current, FlipAnim.ScreenRect.Get(2, current.showParameter));
        }

        private void DrawOverlay()
        {
            for (var i = 0; i < 3; i++)
            {
                if (!current.isPlay || current.PokoType == PokoType.Finger || current.PokoType == PokoType.Tongue)
                {
                    flipAnims[i].Overlay.Stop();
                    return;
                }

                flipAnims[i].Overlay.Draw(mediator.muffs[i].GetOverlayPos(), i, current);
            }
        }

        private void CheckInput()
        {
            if (Input.GetKeyDown(settings.ControllerHotKey))
                controller.showController = !controller.showController;

            if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKey(KeyCode.LeftShift)
                                              && Input.GetKeyDown(settings.ControllerHotKey))
                _debug = !_debug;

            //if (!settings.enablePlugin) 
            //    return;
            //
            //if (controller.showController && settings.enablePokoCam && settings.PokoCam_CustomPos)
            //{
            //    if (Input.GetKey(KeyCode.Mouse0))
            //    {
            //        if (settings.PokoCam_Pos.Contains(Event.current.mousePosition))
            //        {
            //            settings.PokoCam_Pos.center =
            //                Vector2.Lerp(settings.PokoCam_Pos.center, Event.current.mousePosition, 0.25f);
            //        }
            //    }
            //}
        }

        private bool IsShooting()
        {
            if (!current.shotReady)
                return false;
            _playingSE = FindObjectsOfType<AudioSource>()?.Where(x => x.isPlaying);
            if (_playingSE == null)
                return false;

            var count = 0;
            foreach (var se in _playingSE)
            {
                if (se == null)
                    continue;
                if (!se.clip.name.Contains(SE_CumShot)) continue;
                if (current.PlayMode == PlayMode.Multiple)
                    return true;
                count++;
            }

            return count == 1;
        }

        private bool IsShowHideScrolls(string hideName)
        {
            if (_hideScroll == null)
            {
                foreach (var x in FindObjectsOfType<HideScroll>().Where(x => x.isActiveAndEnabled))
                    if (x.name == "Top" && x.ParentObject.name == hideName)
                    {
                        _hideScroll = x;
                        break;
                    }

                return false;
            }

            if (_hideScroll.ParentObject.name != hideName)
                return false;

            return (bool) (_hideScroll.GetType().GetField(NameHideScrollField, bindingFlags)?.GetValue(_hideScroll) ??
                           false);
        }

        private bool IsShowConfigPanel()
        {
            if (configPanel != null)
                return configPanel.activeInHierarchy;

            configPanel = GameObject.Find(ConfigPanel);
            return false;
        }

        private bool IsShowResultPanel()
        {
            if (resultPanel != null)
                return resultPanel.activeInHierarchy;

            resultPanel = GameObject.Find(ResultPanel);
            return false;
        }

        private void CheckScreenFade()
        {
            /*
            if (screenChildren)
            {
                if (screenChildren.fade_status != WfScreenChildren.FadeStatus.Null)
                {
                    if (screenChildren.fade_status != WfScreenChildren.FadeStatus.Wait)
                    {
                        //UnityEngine.Debug.Log($"Fadeout {screenChildren}");
                        mediator.Initialize();
                        script.Initialize();
                    }

                    return;
                }

                screenChildren = null;
            }

            foreach (var children in FindObjectsOfType<WfScreenChildren>())
                if (children && children.fade_status != WfScreenChildren.FadeStatus.Null)
                {
                    screenChildren = children;
                    return;
                }*/

            if (GameMain.Instance.MainCamera.IsFadeOut())
            {
                UnityEngine.Debug.Log($"InOutAnimation: Camera fadeout, reinitializing");
                mediator.Initialize();
                script.Initialize();
            }
        }

        private static void DestroyPluginObjects()
        {
            var objects = FindObjectsOfType<GameObject>();
            foreach (var obj in objects)
                if (obj.name.Contains($"{PluginName}"))
                    DestroyObject(obj);
        }

        private void AllReset()
        {
            foreach (var ini in _initializables)
                ini?.Initialize();
            DestroyPluginObjects();
        }

        #endregion

        #region Classes

        private class Mediator : IInitializable
        {
            private readonly CharacterMgr charaMgr;
            private readonly Maid[] manArray;

            internal readonly Muff[] muffs;
            internal readonly Poko[] pokos;
            private bool initialized;
            private Vector3 maidOffset;
            public int manLength;
            public bool muffsPrepared;
            public bool pokosPrepared;
            public bool SwapTargetNPC;

            public Maid TargetMaid;
            public Maid TargetMan;

            public Mediator()
            {
                charaMgr = GameMain.Instance.CharacterMgr;
                manArray = new Maid[charaMgr.GetManCount()];
                muffs = new Muff[3];
                pokos = new Poko[charaMgr.GetManCount()];
                Initialize();
            }

            public void Initialize()
            {
                if (initialized)
                    return;
                Array.Clear(manArray, 0, manArray.Length);
                Array.Clear(pokos, 0, pokos.Length);
                TargetMaid = null;
                TargetMan = null;
                manLength = 0;
                muffsPrepared = false;
                pokosPrepared = false;
                initialized = true;
            }


            public void FindTarget(State current)
            {
                if (charaMgr.IsBusy())
                    return;

                if (current.PlayMode != PlayMode.Swap || !charaMgr.GetMaid(1).IsValid())
                    SwapTargetNPC = false;

                var maid = current.PlayMode == PlayMode.Harem || SwapTargetNPC
                    ? charaMgr.GetMaid(1)
                    : charaMgr.GetMaid(0);

                if (maid == null)
                {
                    if (TargetMaid.IsValid())
                    {
                        Initialize();
                        current.Initialize();
                    }

                    return;
                }

                TargetMaid = maid;

                manLength = 0;
                for (var i = 0; i < manArray.Length; i++)
                {
                    var man = charaMgr.GetMan(i);

                    //if (man.IsValid() && man.body0.GetChinkoVisible())
                    if (man.IsValid())
                    {
                        manArray[i] = man;
                        manLength++;
                    }
                }

                TargetMan = current.PlayMode == PlayMode.Swap && manArray[1] != null && !SwapTargetNPC
                    ? manArray[1]
                    : manArray[0];

                initialized = false;
            }

            public void PrepareMuffs()
            {
                if (muffsPrepared)
                    return;

                if (!TargetMaid.IsValid())
                {
                    muffsPrepared = false;
                    return;
                }

                if (muffs[0] == null || !maidOffset.Equals(TargetMaid.body0.CenterBone.position))
                {
                    muffs[0] = new Front(MuffType.Front, TargetMaid);
                    muffs[1] = new Back(MuffType.Back, TargetMaid);
                    muffs[2] = new Mouth(MuffType.Mouth, TargetMaid);
                    maidOffset = TargetMaid.body0.CenterBone.position;
                }
                else
                {
                    muffs[0].Set(MuffType.Front, TargetMaid);
                    muffs[1].Set(MuffType.Back, TargetMaid);
                    muffs[2].Set(MuffType.Mouth, TargetMaid);
                }

                muffsPrepared = muffs[0].Maid == TargetMaid;
            }

            public void PreparePokos(State current)
            {
                if (pokosPrepared || !muffsPrepared)
                    return;

                switch (current.PlayMode)
                {
                    case PlayMode.Self:
                        for (var i = 0; i < pokos.Length; i++)
                            if (pokos[i]?.GetType() == typeof(TNP))
                                pokos[i] = null;
                        break;

                    case PlayMode.Normal:
                    case PlayMode.Swap:
                    case PlayMode.Harem:
                        if (TargetMan == null)
                        {
                            pokosPrepared = false;
                            return;
                        }

                        if (!(pokos[0] is TNP t) || !t.man.IsValid())
                            pokos[0] = new TNP(TargetMan);
                        break;

                    case PlayMode.Multiple:
                        for (var i = 0; i < pokos.Length; i++)
                        {
                            if (manArray[i] == null)
                                break;
                            if (!(pokos[i] is TNP g) || !g.man.IsValid())
                                pokos[i] = new TNP(manArray[i]);
                        }

                        pokosPrepared = pokos[manLength > 1 ? manLength - 1 : 0] != null;
                        return;
                }

                if (current.PokoType == PokoType.TNP)
                {
                    pokosPrepared = pokos[0] != null;
                    return;
                }

                var last = pokos.Length - 1;
                switch (current.PokoType)
                {
                    case PokoType.Tongue:
                        if (TargetMan.IsValid())
                            if (!(pokos[last] is Tongue t) || !t.man.IsValid())
                                pokos[last] = new Tongue(TargetMan);
                        pokosPrepared = pokos[last] is Tongue tt && tt.man.IsValid();
                        break;

                    case PokoType.Dildo:
                        if (TargetMaid.IsValid())
                            if (!(pokos[last] is Dildo d) || !d.maid.IsValid())
                                pokos[last] = new Dildo(TargetMaid);
                        pokosPrepared = pokos[last] is Dildo dd && dd.maid.IsValid();
                        break;

                    case PokoType.Vibe:
                        if (!(pokos[last] is Vibe))
                            pokos[last] = new Vibe();

                        pokosPrepared = true;
                        break;

                    case PokoType.Finger:
                        var m = current.isOnani ? TargetMaid : TargetMan;
                        if (!(pokos[last - 1] is Finger f) || !f.maid.IsValid() || f.maid != m)
                        {
                            pokos[last - 1] = new Finger(m, true);
                            pokos[last] = new Finger(m, false);
                        }

                        pokosPrepared = pokos[last - 1] is Finger;
                        break;
                }
            }

            public void ValidatePair(State current)
            {
                if (current.PrimaryMuff == MuffType.None)
                    return;
                switch (current.PlayMode)
                {
                    case PlayMode.Self:
                        if (pokos.Any(poko => poko?.ValidateTargetMuff(muffs[(int) current.PrimaryMuff]) ?? false))
                            return;
                        pokosPrepared = false;
                        return;

                    case PlayMode.Normal:
                    case PlayMode.Swap:
                    case PlayMode.Harem:
                        var n = (int) current.PrimaryMuff == 1 ? 1 : 0;
                        switch (current.PokoType)
                        {
                            case PokoType.Finger:
                                foreach (var poko in pokos)
                                    if (poko is Finger f && f.ValidateTargetMuff(muffs[n]))
                                        return;
                                break;

                            case PokoType.Vibe:
                                foreach (var poko in pokos)
                                    if (poko is Vibe v && v.ValidateTargetMuff(muffs[n]))
                                        return;
                                break;

                            case PokoType.Tongue:
                                foreach (var poko in pokos)
                                    if (poko is Tongue t && t.ValidateTargetMuff(muffs[0]))
                                        return;
                                break;

                            case PokoType.TNP:
                                if (pokos[0] is TNP p && p.ValidateTargetMuff(muffs[(int) current.PrimaryMuff]))
                                    return;
                                break;
                        }

                        pokosPrepared = false;
                        return;

                    case PlayMode.Multiple:
                        pokos[0]?.ValidateTargetMuff(muffs[(int) current.PrimaryMuff]);

                        switch (current.MuffNum)
                        {
                            case MuffNum.Double:
                                pokos[1]?.ValidateTargetMuff(muffs[1 - (int) current.PrimaryMuff]);
                                return;

                            case MuffNum.Triple:
                            case MuffNum.Unknown:
                                for (var i = 1; i < manLength; i++)
                                {
                                    if (pokos[i] == null)
                                        continue;
                                    for (var j = 2; j >= 0; j--)
                                    {
                                        if (j == (int) current.PrimaryMuff)
                                            continue;
                                        if (pokos[i].ValidateTargetMuff(muffs[j]))
                                            break;
                                    }
                                }

                                break;
                        }

                        break;
                }
            }

            public void UpdateValues(State current)
            {
                for (var i = 0; i < muffs.Length; i++)
                {
                    var value = 0.0f;
                    foreach (var poko in pokos)
                    {
                        if (poko == null || !poko.Validated)
                            continue;
                        if ((int) poko.TargetMuff != i) continue;
                        var _v = muffs[i].GetInsertRate(current, poko);
                        value = _v > value ? _v : value;
                        var n = (int) current.PrimaryMuff == 1 ? 1 : 0;
                        if ((current.PokoType == PokoType.Finger || current.PokoType == PokoType.Tongue)
                            && i == n && value > 0.01f)
                            value = settings.Morpher_Threshold * 0.5f;
                    }

                    current.rates[i] = Mathf.SmoothStep(current.rates[i], value, settings.Anim_Speed);
                    muffs[i].SetBlendValue(current.rates[i]);
                }
            }
        }

        public class State : IInitializable
        {
            private string _recent;
            public bool isOnani;
            public bool isPlay;
            public bool isShooting, showSkillSelect, showParameter, showConfigPanel, showResultPanel, CanDrawOverlay;
            internal MuffNum MuffNum;
            internal PlayMode PlayMode;
            internal PlayState PlayState;
            internal PokoType PokoType;
            internal MuffType PrimaryMuff;
            internal InsertRates rates = new InsertRates();

            public State()
            {
                Initialize();
            }

            public string CurrentSkill
            {
                get => _recent;
                set
                {
                    if (_recent != value)
                        Initialize();
                    _recent = value;
                }
            }

            public bool shotReady => PlayState == PlayState.Shot || PlayState == PlayState.ShotFin;

            public void Initialize()
            {
                isPlay = false;
                isOnani = false;
                isShooting = false;
                showSkillSelect = false;
                showParameter = false;
                CanDrawOverlay = false;
                PlayMode = PlayMode.Normal;
                MuffNum = MuffNum.Unknown;
                PlayState = PlayState.None;
                PrimaryMuff = MuffType.None;
                PokoType = PokoType.TNP;
                rates.Clear();
            }
        }

        public class InsertRates
        {
            private readonly float[] rate;

            public InsertRates()
            {
                rate = new float[3];
            }

            public float this[int i]
            {
                get => rate[i];
                set => rate[i] = value > 0.001f ? value : 0.0f;
            }

            public void Clear()
            {
                Array.Clear(rate, 0, 3);
            }
        }

        private class AnmScript : IInitializable
        {
            private const string C_ = "C_", A_ = "A_", A = "A";
            private const string _2ana = "2ana", _3ana = "3ana";

            private const string kunni = "kunni",
                dildo = "dildo",
                vibe = "vibe",
                aibu = "aibu",
                onani = "onani",
                _tubo = "_tubo";

            private const string _in = "_in", _taiki = "_taiki";

            private const string _ONCE = "_ONCE",
                _once = "_once",
                _shaseigo = "_shaseigo",
                _zeccyougo = "_zeccyougo",
                _tanetukego = "_tanetukego";

            private const string seijyou = "seijyou",
                taimen = "taimen",
                kouhai = "kouhai",
                haimen = "haimen",
                sokui = "sokui";

            private static readonly string[] ValidSkillNames =
            {
                "seijyou", "kouhai", "sokui", "kijyoui", "[th]aimenzai", "ritui", "manguri", "hekimen", "tinguri",
                "ekiben", "turusi", "matuba", "syumoku", "mzi", "kakae", "tekoki", "tekoona", "fera", "sixnine",
                "self_ir", "housi",
                "onani", "paizuri", "aibu", "kunni", "mp_arai", "arai2", "kousoku", "vibe", "mokuba", "harituke",
                "ran\\dp", "muri.*\\dp", ".*\\dp_", "sex"
            };

            private static readonly Regex PatternValidSkillNames =
                new Regex($@"{string.Join("|", ValidSkillNames)}", RegexOptions.Compiled);

            private readonly State _current;

            private readonly Regex patternMuffMouth =
                new Regex(@"fera|sixnine|self_ir|_kuti|housi", RegexOptions.Compiled);

            private readonly Regex patternMultiple = new Regex(@"(ran|muri|kousoku).*\dp", RegexOptions.Compiled);
            private readonly Regex patternNoneBreak = new Regex(@"name|suri|koki|tama|sumata", RegexOptions.Compiled);
            private readonly Regex patternNoneOutside = new Regex(@"^.*(_soto|_kao).*$", RegexOptions.Compiled);
            private readonly Regex patternPlaying = new Regex(@"^.*_\d([ab]0[12])?_|_gr|_momi", RegexOptions.Compiled);

            private readonly Regex Separator =
                new Regex(@"_taiki|_in|_ONCE|_once|_shaseigo|_tanetukego|_zeccyougo|_sissin|_\d[^p]",
                    RegexOptions.Compiled);

            public string MaidAnmName, ManAnmName;


            public AnmScript(State current)
            {
                _current = current;
                Initialize();
            }

            public void Initialize()
            {
                _current.Initialize();
            }

            public void Parse(Mediator med)
            {
                if (!med.TargetMaid.IsValid() || med.TargetMan == null)
                    return;
                MaidAnmName = med.TargetMaid.body0.LastAnimeFN;
                ManAnmName = med.manLength > 0
                    ? med.TargetMan.body0.LastAnimeFN
                    : "";

                if (!PatternValidSkillNames.IsMatch(MaidAnmName))
                {
                    if (_current.isPlay)
                    {
                        _current.Initialize();
                        med.Initialize();
                        DestroyPluginObjects();
                    }

                    _current.CurrentSkill = null;
                    return;
                }

                _current.isPlay = true;
                _current.CurrentSkill = ParseCurrentSkill();

                var muff = _current.PrimaryMuff;
                var poko = _current.PokoType;
                var ona = _current.isOnani;

                if (!string.IsNullOrEmpty(_current.CurrentSkill))
                {
                    _current.PlayMode = ParsePlayMode();
                    _current.PrimaryMuff = ParsePrimary();
                    _current.MuffNum = ParseMuffNum();
                }

                _current.isOnani = MaidAnmName.Contains(onani);
                _current.PokoType = ParsePokoType();
                _current.PlayState = ParsePlayState();

                if (muff != _current.PrimaryMuff || poko != _current.PokoType || ona != _current.isOnani)
                    med.Initialize();

                return;


                string ParseCurrentSkill()
                {
                    if (!string.IsNullOrEmpty(_current.CurrentSkill) && MaidAnmName.Contains(_current.CurrentSkill))
                        return _current.CurrentSkill;
                    if (Separator.IsMatch(MaidAnmName))
                        return Separator.Split(MaidAnmName)[0];
                    return "";
                }

                PlayMode ParsePlayMode()
                {
                    if (MaidAnmName.Contains(C_) || ManAnmName.Contains(C_))
                        return PlayMode.Harem;
                    if (patternMultiple.IsMatch(_current.CurrentSkill))
                        return PlayMode.Multiple;

                    switch (med.manLength)
                    {
                        case 0: return PlayMode.Self;
                        case 2: return PlayMode.Swap;
                        default: return PlayMode.Normal;
                    }
                }

                MuffType ParsePrimary()
                {
                    if (_current.PlayMode != PlayMode.Multiple && patternMuffMouth.IsMatch(MaidAnmName))
                        return MuffType.Mouth;
                    if (_current.CurrentSkill.Contains(A) || MaidAnmName.Contains(A_) || ManAnmName.Contains(A_))
                        return MuffType.Back;
                    return MuffType.Front;
                }

                MuffNum ParseMuffNum()
                {
                    var str = _current.CurrentSkill;
                    if (str.Contains(_2ana))
                        return MuffNum.Double;
                    if (str.Contains(_3ana))
                        return MuffNum.Triple;
                    return MuffNum.Unknown;
                }

                PokoType ParsePokoType()
                {
                    return MaidAnmName switch
                    {
                        { } s when s.Contains(kunni) => PokoType.Tongue,
                        { } s when s.Contains(dildo) => PokoType.Dildo,
                        { } s when s.Contains(vibe) => PokoType.Vibe,
                        { } s when s.Contains(aibu) || s.Contains(onani) || s.Contains(_tubo) => PokoType.Finger,
                        _ => PokoType.TNP
                    };
                }

                PlayState ParsePlayState()
                {
                    if (MaidAnmName.Contains(_in))
                    {
                        if (_current.PlayState != PlayState.Insert)
                            med.Initialize();
                        return PlayState.Insert;
                    }

                    if (MaidAnmName.Contains(_taiki))
                        return PlayState.Wait;
                    if (patternNoneBreak.IsMatch(MaidAnmName) || patternNoneOutside.IsMatch(ManAnmName))
                        return PlayState.Eject;
                    if (MaidAnmName.Contains(_ONCE) || MaidAnmName.Contains(_once))
                        return PlayState.Shot;
                    if (MaidAnmName.Contains(_shaseigo) || MaidAnmName.Contains(_tanetukego)
                                                        || MaidAnmName.Contains(_zeccyougo))
                        return PlayState.ShotFin;
                    if (patternPlaying.IsMatch(MaidAnmName))
                        return PlayState.Play;

                    return PlayState.None;
                }
            }

            internal sPosition GetsPosition(MuffType muffType)
            {
                if (_current.PlayMode != PlayMode.Self)
                    if (muffType == MuffType.Front || muffType == MuffType.Back)
                    {
                        if (MaidAnmName.Contains(seijyou) || MaidAnmName.Contains(taimen))
                            return sPosition.Normal;
                        if (MaidAnmName.Contains(kouhai) || MaidAnmName.Contains(haimen))
                            return sPosition.Dog;
                        if (MaidAnmName.Contains(sokui))
                            return sPosition.Side;
                    }

                return sPosition.None;
            }
        }


        #region Muffs

        private class Front : Muff
        {
            internal Front(MuffType type, Maid maid)
            {
                Set(type, maid);
            }

            internal override void Set(MuffType type, Maid maid)
            {
                colliderSize = 1.0f;
                this.type = type;
                Maid = maid;
                body = Maid.body0;

                if (collider == null)
                    collider = new MuffCollider(this.type);
                else
                    collider.Set(this.type);

                if (morpher == null)
                    morpher = new MuffMorpher(this.type, body);
                else
                    morpher.Set(this.type, body);
                positions = new Positions();
            }

            protected override void CheckTransforms()
            {
                if (!_root)
                    _root = body.Spine
                        ? body.Spine
                        : body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Spine0);

                if (!_mid)
                    _mid = body.Pelvis
                        ? body.Pelvis
                        : body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Pelvis);

                if (!_top)
                    _top = body.GetBone(VaginaBoneName);
                if (!_other)
                    _other = body.GetBone(AnalBoneName);
            }

            public override Positions GetOverlayPos()
            {
                var cameraMain = GameMain.Instance.MainCamera;
                Update();

                var position = _top.position;
                var start = position;
                var end = _root.position - (_other.position - position) * 0.5f;
                var dir = start - end;
                start -= (start - cameraMain.GetPos()) * settings.Overlay_CameraDistance;
                end = end - dir * settings.Overlay_LineScale
                          - (end - cameraMain.GetPos()) * settings.Overlay_CameraDistance;

                start += dir.normalized * settings.Overlay_DirectionalOffset;
                end += dir.normalized * settings.Overlay_DirectionalOffset;

                positions.Top = start;
                positions.Root = end;

                return positions;
            }

            public override float GetInsertRate(State current, Poko poko)
            {
                if (current.PlayState == PlayState.Wait || current.PlayState == PlayState.None)
                    return 0.0f;
                Update();
                var pos = poko.GetPokoPos();
                var dstTop2mid = Distance(pos.Top, _mid.position);
                var tnkLength = Distance(pos.Root, pos.Top);
                if (__length < 0.001f)
                    __length = Mathf.Sqrt(tnkLength);

                var position = _top.position;
                var dstRoot2top = Distance(pos.Root, position);
                var muffDepth = Distance(position, _root.position);
                if (tnkLength >= dstRoot2top && muffDepth >= dstTop2mid)
                {
                    if (poko._lastTime > Time.frameCount)
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));

                    if (collider.ContainsAny(pos))
                    {
                        poko._lastTime = Time.frameCount + 5;
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));
                    }
                }

                return 0.0f;
            }
        }

        private class Back : Muff
        {
            internal Back(MuffType type, Maid maid)
            {
                Set(type, maid);
            }

            internal override void Set(MuffType type, Maid maid)
            {
                colliderSize = 1.0f;
                this.type = type;
                Maid = maid;
                body = Maid.body0;

                if (collider == null)
                    collider = new MuffCollider(this.type);
                else
                    collider.Set(this.type);

                if (morpher == null)
                    morpher = new MuffMorpher(this.type, body);
                else
                    morpher.Set(this.type, body);
                positions = new Positions();
            }

            protected override void CheckTransforms()
            {
                if (!_root)
                    _root = body.Spine
                        ? body.Spine
                        : body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Spine0);

                if (!_mid)
                    _mid = body.Pelvis
                        ? body.Pelvis
                        : body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Pelvis);

                if (!_top)
                    _top = body.GetBone(AnalBoneName);
                if (!_other)
                    _other = body.GetBone(VaginaBoneName);
            }

            public override Positions GetOverlayPos()
            {
                var cameraMain = GameMain.Instance.MainCamera;
                Update();

                var position = _top.position;
                var start = position;
                var end = _root.position - (_other.position - position) * 0.5f;
                var dir = start - end;
                start -= (start - cameraMain.GetPos()) * settings.Overlay_CameraDistance;
                end = end - dir * settings.Overlay_LineScale
                          - (end - cameraMain.GetPos()) * settings.Overlay_CameraDistance;

                start += dir.normalized * settings.Overlay_DirectionalOffset;
                end += dir.normalized * settings.Overlay_DirectionalOffset;

                positions.Top = start;
                positions.Root = end;

                return positions;
            }

            public override float GetInsertRate(State current, Poko poko)
            {
                if (current.PlayState == PlayState.Wait || current.PlayState == PlayState.None)
                    return 0.0f;
                Update();
                var pos = poko.GetPokoPos();
                var dstTop2mid = Distance(pos.Top, _mid.position);
                var tnkLength = Distance(pos.Root, pos.Top);
                if (__length < 0.001f)
                    __length = Mathf.Sqrt(tnkLength);

                var dstRoot2top = Distance(pos.Root, _top.position);
                var muffDepth = Distance(_top.position, _root.position);
                if (tnkLength >= dstRoot2top && muffDepth >= dstTop2mid)
                {
                    if (poko._lastTime > Time.frameCount)
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));

                    if (collider.ContainsAny(pos))
                    {
                        poko._lastTime = Time.frameCount + 5;
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));
                    }
                }

                return 0.0f;
            }
        }

        private class Mouth : Muff
        {
            internal static readonly Vector3 mouthOffset = new Vector3(0, -0.02f, 0.08f);

            internal Mouth(MuffType type, Maid maid)
            {
                Set(type, maid);
            }

            internal override void Set(MuffType type, Maid maid)
            {
                this.type = type;
                Maid = maid;
                body = Maid.body0;

                if (collider == null)
                    collider = new MuffCollider(this.type);
                else
                    collider.Set(this.type);

                positions = new Positions();
                colliderSize = 1.75f;
            }

            protected override void CheckTransforms()
            {
                if (_top == null)
                {
                    _top = new GameObject($"{PluginName}__mouth__").transform;
                    var parent = body.GetBone(MouthBoneName);
                    _top.SetParent(parent);
                    _top.SetPositionAndRotation(parent.position, parent.rotation);
                    _top.Translate(mouthOffset);
                }

                if (_root == null)
                    _root = body.trsHead;
            }

            public override Positions GetOverlayPos()
            {
                if (_top == null || _root == null)
                    return positions;

                var cameraMain = GameMain.Instance.MainCamera;
                Update();
                var start = _top.position;
                var end = _root.position - (_top.position - _root.position) * 0.5f;
                var dir = start - end;
                start -= (start - cameraMain.GetPos()) * settings.Overlay_CameraDistance;
                end = end - dir * settings.Overlay_LineScale
                          - (end - cameraMain.GetPos()) * settings.Overlay_CameraDistance;

                start += dir.normalized * settings.Overlay_DirectionalOffset;
                end += dir.normalized * settings.Overlay_DirectionalOffset;

                positions.Top = start;
                positions.Root = end;

                return positions;
            }

            public override float GetInsertRate(State current, Poko poko)
            {
                if (current.PlayState == PlayState.Wait || current.PlayState == PlayState.None)
                    return 0.0f;
                Update();
                var pos = poko.GetPokoPos();
                var tnkLength = Distance(pos.Root, pos.Top);
                if (__length < 0.001f)
                    __length = Mathf.Sqrt(tnkLength);

                var dstRoot2top = Distance(pos.Root, _top.position);
                if (tnkLength >= dstRoot2top)
                {
                    if (poko._lastTime > Time.frameCount)
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));

                    if (collider.ContainsAny(pos))
                    {
                        poko._lastTime = Time.frameCount + 5;
                        return Mathf.InverseLerp(__length, 0, Mathf.Sqrt(dstRoot2top));
                    }
                }

                return 0.0f;
            }
        }

        public abstract class Muff
        {
            internal const string VaginaBoneName = "_IK_vagina",
                AnalBoneName = "_IK_anal",
                MouthBoneName = "Mouth";

            protected float __length;
            protected Transform _top, _mid, _root, _other;
            protected TBody body;
            protected MuffCollider collider;
            protected float colliderSize;

            public Maid Maid;
            internal MuffMorpher morpher;
            protected Positions positions;
            internal MuffType type;

            private float depth => Vector3.Distance(_top.position, _root.position);

            protected abstract void CheckTransforms();
            public abstract Positions GetOverlayPos();
            internal abstract void Set(MuffType type, Maid maid);

            public MuffCollider GetCollider()
            {
                Update();
                return collider;
            }

            protected void Update()
            {
                if (!Maid.IsValid())
                    return;
                CheckTransforms();
                collider.SetTransform(_top, _root != null ? _root : _mid, depth * colliderSize);
            }

            public void SetBlendValue(float value)
            {
                if (!settings.enableMorpher)
                    return;
                morpher?.SetBlendEx(value);
                if (!settings.Morpher_EnableFix)
                    return;
                morpher?.FixBlendEx();
            }

            protected static float Distance(Vector3 from, Vector3 to)
            {
                var xD = from.x - to.x;
                var yD = from.y - to.y;
                var zD = from.z - to.z;
                return xD * xD + yD * yD + zD * zD;
            }

            public abstract float GetInsertRate(State current, Poko poko);
        }

        public class MuffCollider
        {
            private const float distance = 0.55f;
            private BoxCollider collider;
            private GameObject obj;
            private MeshRenderer renderer;
            private Vector3 scale;
            private Transform start, end;
            private MuffType type;

            internal MuffCollider(MuffType type)
            {
                Set(type);
            }

            internal void Set(MuffType type)
            {
                this.type = type;
            }

            public void SetTransform(Transform start, Transform end, float length)
            {
                this.start = start;
                this.end = end;
                scale.Set(length * 0.5f, length * 0.5f, length);
            }

            private bool SetUpCollider()
            {
                if (!start || !end)
                    return false;

                if (obj == null)
                {
                    obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    renderer = obj.GetComponent<MeshRenderer>();
                    obj.name = $"{PluginName}__muffcol__{type}";
                    collider = null;
                }
                else
                {
                    if (collider == null)
                    {
                        collider = obj.GetComponent<BoxCollider>() ?? obj.AddComponent<BoxCollider>();
                        collider.isTrigger = true;
                    }
                }

                renderer.enabled = _debug;

                return obj && collider;
            }

            public bool Contains(Vector3 point)
            {
                if (start.position != end.position && SetUpCollider())
                {
                    var rot = Quaternion.LookRotation(start.position - end.position);
                    var pos = start.position - (start.position - end.position) * distance;
                    collider.transform.SetPositionAndRotation(pos, rot);
                    collider.transform.localScale = scale;
                    return collider.ClosestPoint(point) == point;
                }

                return false;
            }

            public bool ContainsAny(Positions pos)
            {
                return Contains(pos.Top) || Contains(pos.Mid) || Contains(pos.Root);
            }

            public bool ContainsAny(params Vector3[] points)
            {
                return points.Any(Contains);
            }

            ~MuffCollider()
            {
                DestroyObject(collider);
                DestroyObject(renderer);
                DestroyObject(obj);
            }
        }

        internal class MuffMorpher
        {
            private TBody body;
            private HashSet<int> keyAvailableSlot;
            private string lastKey;
            private float lastValue;
            private MuffType type;

            public MuffMorpher(MuffType type, TBody body)
            {
                Set(type, body);
            }

            private string Key
            {
                get
                {
                    var ret = type == MuffType.Front
                        ? settings.Morpher_KeyV
                        : settings.Morpher_KeyA;
                    if (lastKey != ret)
                        CheckKeyAvailable(ret);

                    return lastKey = ret;
                }
            }

            public void Set(MuffType type, TBody body)
            {
                lastValue = 0.0f;
                if (keyAvailableSlot == null)
                    keyAvailableSlot = new HashSet<int>();
                else
                    keyAvailableSlot.Clear();
                this.body = body;
                this.type = type;
                CheckKeyAvailable(Key);
            }

            private void CheckKeyAvailable(string key)
            {
                keyAvailableSlot.Clear();
                if (body == null) return;
                for (var i = 0; i < (int) TBody.SlotID.end; i++)
                    if (body.goSlot?[i]?.morph is TMorph m && m.hash.ContainsKey(key))
                        keyAvailableSlot.Add(i);
            }

            public void SetBlendEx(float value)
            {
                if (!settings.enableMorpher || keyAvailableSlot.Count < 1)
                    return;

                var result = Mathf.Clamp(
                    Mathf.InverseLerp(0.0f, settings.Morpher_Threshold, value),
                    settings.Morpher_Min,
                    settings.Morpher_Max
                );

                foreach (var slot in keyAvailableSlot)
                    if (body.goSlot[slot]?.morph?.hash[Key] is int idx)
                    {
                        body.goSlot[slot].morph.SetBlendValues(idx, result);
                        lastValue = result;
                    }
                    else
                    {
                        CheckKeyAvailable(Key);
                        return;
                    }
            }

            public void FixBlendEx()
            {
                if (!settings.Morpher_EnableFix || keyAvailableSlot.Count < 1 || type == MuffType.Back)
                    return;

                if (Time.frameCount % Mathf.RoundToInt(settings.Morpher_Waitframe) != 0) return;
                foreach (var i in keyAvailableSlot)
                    body.goSlot[i]?.morph?.FixBlendValues();
            }
        }

        #endregion


        #region Pokos

        private class TNP : Poko
        {
            private const string TnpTopName = "chinko_nub",
                TnpMidName = "chinko2",
                TnpRootName = "chinko1";

            public readonly Maid man;
            private Transform tnkTop, tnkMid, tnkRoot;

            public TNP(Maid man)
            {
                this.man = man;
                Validated = false;
                TargetMuff = MuffType.None;
            }

            protected override void UpdateTransforms()
            {
                foreach (var trs in man.body0.GetComponentsInChildren<Transform>())
                {
                    if (trs == null)
                        continue;

                    switch (trs.name)
                    {
                        case TnpTopName:
                            tnkTop = trs;
                            break;
                        case TnpRootName:
                            tnkRoot = trs;
                            break;
                        case TnpMidName:
                            tnkMid = trs;
                            break;
                    }

                    if (tnkTop && tnkRoot && tnkMid)
                    {
                        positions.Top = tnkTop.position;
                        positions.Mid = tnkMid.position;
                        positions.Root = tnkRoot.position;
                        return;
                    }
                }
            }
        }

        private class Dildo : Poko
        {
            private const string DildoName = "ディルド＆台";
            private readonly Transform[] _transforms;
            public readonly Maid maid;

            public Dildo(Maid maid)
            {
                this.maid = maid;
                _transforms = new Transform[4];
                Validated = false;
                TargetMuff = MuffType.None;
            }

            protected override void UpdateTransforms()
            {
                if (_transforms[0] == null)
                {
                    _transforms[0] = GameObject.Find(DildoName)?.transform;
                    if (_transforms[0] != null)
                        return;
                    foreach (var x in FindObjectsOfType<Transform>())
                    {
                        if (x == null)
                            continue;
                        if (x.name.Contains(DildoName))
                        {
                            _transforms[0] = x;
                            break;
                        }
                    }
                }
                else
                {
                    if (!maid.IsValid())
                        return;
                    if (!_transforms[1])
                        for (var i = 1; i < 4; i++)
                        {
                            if (_transforms[i] != null)
                                continue;
                            _transforms[i] = new GameObject($"{PluginName}__Dildo_{i}").transform;
                            _transforms[i].SetParent(_transforms[i - 1]);
                            _transforms[i].Translate(0.0f, 0.13f * i, 0.0f);
                        }

                    var offset = maid.body0.CenterBone.position;
                    positions.Top = _transforms[3].position + offset;
                    positions.Mid = _transforms[2].position + offset;
                    positions.Root = _transforms[1].position + offset;
                }
            }
        }

        private class Vibe : Poko
        {
            private const string VibeName = "vibe&cli", AnalVibeName = "analvibe", BigVibeName = "Predator";
            private const float HalfLength = 0.1f;
            private Transform _transform;

            protected override void UpdateTransforms()
            {
                if (_transform == null)
                    _transform = GameObject.Find(VibeName)?.transform;
                if (_transform == null)
                    _transform = GameObject.Find(AnalVibeName)?.transform;
                if (_transform == null)
                    _transform = GameObject.Find(BigVibeName)?.transform;

                if (_transform == null)
                    return;

                var dir = _transform.right;
                positions.Root = _transform.position;
                positions.Mid = positions.Root + dir * HalfLength;
                positions.Top = positions.Mid + dir * HalfLength;
            }
        }

        private class Tongue : Poko
        {
            private const string HeadTopName = "mheadbone_nub";
            private readonly Transform[] _transforms;
            public readonly Maid man;

            public Tongue(Maid man)
            {
                this.man = man;
                _transforms = new Transform[2];
                Validated = false;
                TargetMuff = MuffType.None;
            }

            protected override void UpdateTransforms()
            {
                if (!man.IsValid())
                    return;

                _transforms[0] = man.body0.GetComponentsInChildren<Transform>()
                    .FirstOrDefault(x => x.name.Contains(HeadTopName));

                if (_transforms[1] == null)
                {
                    _transforms[1] = new GameObject($"{PluginName}__mouth__").transform;
                    var parent = man.body0.GetBone(Muff.MouthBoneName);
                    _transforms[1].SetParent(parent);
                    _transforms[1].SetPositionAndRotation(parent.position, parent.rotation);
                    _transforms[1].Translate(Mouth.mouthOffset);
                }

                positions.Top = _transforms[0].position;
                positions.Root = _transforms[1].position;
            }
        }

        private class Finger : Poko
        {
            private readonly Transform[] _transforms;
            public readonly Maid maid;
            private readonly bool righthand;

            public Finger(Maid maid, bool righthand)
            {
                this.maid = maid;
                this.righthand = righthand;
                _transforms = new Transform[4];
            }

            protected override void UpdateTransforms()
            {
                if (!maid.IsValid())
                    return;

                if (_transforms[0] == null)
                {
                    _transforms[0] = righthand
                        ? maid.body0.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Hand_R)
                            .GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.Contains("R Finger2"))
                        : maid.body0.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Hand_L)
                            .GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.Contains("L Finger2"));
                }
                else
                {
                    _transforms[1] = GetFirstNonSCLChild(_transforms[0]);
                    _transforms[2] = GetFirstNonSCLChild(_transforms[1]);
                    _transforms[3] = GetFirstNonSCLChild(_transforms[2]);

                    positions.Top = _transforms[3].position;
                    positions.Mid = _transforms[2].position;
                    positions.Root = _transforms[1].position;
                }
            }

            private Transform GetFirstNonSCLChild(Transform t)
            {
                var count = t.childCount;
                for (var i = 0; i < count; i++)
                {
                    var child = t.GetChild(i);
                    if (!child.name.Contains("_SCL_"))
                        return child;
                }

                return t;
            }
        }

        public abstract class Poko
        {
            internal int _lastTime;
            protected Positions positions;
            internal MuffType TargetMuff;
            protected PokoTrail[] trails;
            public bool Validated;

            protected Poko()
            {
                positions = new Positions();
                TargetMuff = MuffType.None;
            }

            protected abstract void UpdateTransforms();

            public bool ValidateTargetMuff(Muff muff)
            {
                if (Validated)
                    return true;
                UpdateTransforms();
                if (muff.GetCollider().ContainsAny(positions))
                {
                    TargetMuff = muff.type;
                    Validated = true;
                }

                return Validated;
            }

            public Positions GetPokoPos()
            {
                UpdateTransforms();
                return positions;
            }

            public void DrawTrails()
            {
                if (trails == null)
                    trails = new[] {new PokoTrail(), new PokoTrail(), new PokoTrail()};
                trails[0].Draw(positions.Top, Color.cyan, Color.blue);
                trails[1].Draw(positions.Mid, Color.magenta, Color.red);
                trails[2].Draw(positions.Root, Color.yellow, Color.green);
            }
        }

        #endregion

        public struct Positions
        {
            public Vector3 Top, Mid, Root;
        }

        public class FlipAnim : IInitializable
        {
            private const int CumInterval = 200;

            private static readonly Regex pattern_searchtex = new Regex(@"^.*\.(bmp|gif|tiff|jpeg|jpg|png)$");

            private static readonly Texture2D blacktex = Texture2D.blackTexture;

            public static ScreenRect ScreenRect = new ScreenRect();
            private static Color alpha = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            private readonly Stopwatch sw;

            public int MaxFrame, CurrentFrame;

            public Overlay Overlay;
            private DirectoryInfo srcPath, srcPathEx;
            private Texture2D[] textures, texturesEx;

            public FlipAnim(DirectoryInfo dir)
            {
                textures = new Texture2D[0];
                srcPath = dir;
                TextureLoaded = false;
                texturesEx = new Texture2D[0];
                srcPathEx = new DirectoryInfo($@"{srcPath.FullName}\ex");
                TextureLoadedEx = false;
                Overlay = new Overlay(this);
                sw = new Stopwatch();

                CheckDirExists();
                Load();
            }

            public bool TextureLoaded { get; private set; }

            public bool TextureLoadedEx { get; private set; }

            public void Initialize()
            {
                if (!TextureLoaded)
                    return;

                CurrentFrame = 0;
                ScreenRect.Set(textures[0].width, textures[0].height);
                MaxFrame = textures.Length;
            }

            ~FlipAnim()
            {
                textures = null;
                texturesEx = null;
                srcPath = null;
                srcPathEx = null;
            }

            private bool LoadTextures(DirectoryInfo dir, ref Texture2D[] array)
            {
                var fileInfos = dir.GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(x => pattern_searchtex.IsMatch(x.Name));
                var infos = fileInfos as FileInfo[] ?? fileInfos.ToArray();
                if (!infos.Any())
                    return false;

                array = new Texture2D[infos.Length];
                for (var i = 0; i < infos.Length; i++)
                {
                    if (!infos[i].Exists)
                        return false;
                    var data = File.ReadAllBytes(infos[i].FullName);

                    array[i] = new Texture2D(1, 1, TextureFormat.ARGB32, true);
                    if (array[i].LoadImage(data))
                        array[i].Apply();
                }

                return true;
            }

            private void CheckDirExists()
            {
                if (srcPath.Exists) return;
                srcPath.Create();
                if (!srcPathEx.Exists)
                    srcPathEx.Create();
            }

            public void Load()
            {
                if (srcPath == null)
                    return;

                CheckDirExists();
                TextureLoaded = LoadTextures(srcPath, ref textures);

                if (srcPathEx == null)
                    return;

                TextureLoadedEx = LoadTextures(srcPathEx, ref texturesEx);

                Initialize();
            }

            internal Texture2D GetCurrentTex()
            {
                return !TextureLoaded ? blacktex : textures[CurrentFrame];
            }

            internal Texture2D StartCumAnimation(bool shot)
            {
                if (!shot)
                {
                    sw.Stop();
                    return GetCurrentTex();
                }

                if (!sw.IsRunning)
                {
                    sw.Reset();
                    sw.Start();
                }
                else
                {
                    var max = texturesEx.Length;
                    var rateEx = Mathf.InverseLerp(0, CumInterval, sw.ElapsedMilliseconds);

                    if (rateEx <= 1.0f)
                    {
                        if (TextureLoadedEx && max > 1)
                            return texturesEx[Mathf.RoundToInt((max - 1) * rateEx)];
                    }
                    else
                    {
                        sw.Stop();
                    }
                }

                return GetCurrentTex();
            }

            public void Draw(int number, State current)
            {
                CurrentFrame = Mathf.RoundToInt((MaxFrame - 1) * current.rates[number]);
                if (!settings.enableOnGUI)
                    return;

                alpha.a = current.showSkillSelect || current.showConfigPanel || current.showResultPanel
                    ? 0.0f
                    : settings.OnGUI_transparency * Mathf.Clamp01(current.rates[number] * 10);

                var texture = current.shotReady
                    ? StartCumAnimation(current.isShooting)
                    : GetCurrentTex();

                GUI.color = alpha;
                GUI.DrawTexture(ScreenRect.Get(number, current.showParameter), texture, ScaleMode.StretchToFill, true,
                    0.0f);
                GUI.color = Color.white;
            }
        }

        public class ScreenRect
        {
            private float aspect;
            private Rect rect = Rect.zero;
            private int width, height;

            public void Set(int width, int height)
            {
                this.width = width;
                this.height = height;
                aspect = height != 0 ? (float) this.width / this.height : 1.0f;
            }

            public Rect Get(int n, bool flag)
            {
                if (aspect < 0.001f && aspect > -0.001f)
                    aspect = (float) Screen.width / Screen.height;
                var stg = settings;
                var h = Screen.height * 0.2f;
                var w = rect.height * aspect;

                var x = flag
                    ? Screen.width - rect.width - Screen.width * 0.17f
                    : Screen.width - rect.width - Screen.width * 0.0625f;
                var y = Screen.height - (rect.height + stg.OnGUI_offset) * (n + 1) - Screen.height * 0.111f;

                rect.Set(x + stg.OnGUI_x_offset, y + stg.OnGUI_y_offset,
                    w * stg.OnGUI_x_scale, h * stg.OnGUI_y_scale);

                return rect;
            }
        }

        public class Overlay
        {
            public const int OverlayLayer = 13;
            private readonly FlipAnim flipAnim;
            private Color color = new Color(1, 1, 1, 1);
            private LineRenderer line;
            private Material material;
            private Shader sprite;

            public Overlay(FlipAnim flipAnim)
            {
                this.flipAnim = flipAnim;
            }

            private bool SetUpLine()
            {
                if (line == null)
                {
                    line = new GameObject($"{PluginName}__Overlay").AddComponent<LineRenderer>();
                    material = null;
                    sprite = null;
                    color = Color.white;
                    line.enabled = false;
                }
                else
                {
                    line.gameObject.layer = OverlayLayer;
                    line.numCapVertices = 0;
                    line.shadowCastingMode = ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.alignment = LineAlignment.View;
                    line.textureMode = LineTextureMode.Stretch;
                    line.useWorldSpace = true;

                    sprite = Shader.Find("Sprites/Default");

                    if (material == null && sprite)
                    {
                        material = line.material;
                        material.renderQueue = 3030;
                        material.shader = sprite;
                        material.color = color;
                    }
                }

                return line && material && sprite;
            }

            public void Draw(Positions pos, int number, State current)
            {
                if (!settings.enableOverlay)
                {
                    Stop();
                    return;
                }

                var texture = current.shotReady
                    ? flipAnim.StartCumAnimation(current.isShooting)
                    : flipAnim.GetCurrentTex();

                if (texture == null || !SetUpLine())
                {
                    Stop();
                    return;
                }

                color.a = settings.Overlay_Transparency *
                          (current.showSkillSelect || current.showConfigPanel || current.showResultPanel
                              ? 0
                              : Mathf.Clamp01(current.rates[number] * 10));

                material.renderQueue = (int) settings.Overlay_RenderQueue;
                material.mainTexture = texture;

                var aspect = texture.height == 0
                    ? (float) texture.width / texture.height
                    : 1.0f;
                var dist = Vector3.Distance(pos.Top, pos.Root);

                line.SetPosition(0, pos.Top);
                line.SetPosition(1, pos.Root);
                line.startWidth = dist / aspect * settings.Overlay_LineWidth * 0.5f;
                line.endWidth = dist / aspect * settings.Overlay_LineWidth * 0.5f;

                line.enabled = true;

                if (material)
                    material.color = color;
            }

            public void Stop()
            {
                if (line == null)
                    return;
                line.enabled = false;
            }
        }


        #region Cams

        private abstract class IOCam : IInitializable
        {
            public const int ManLayer = 9;
            protected static GameObject bgObject;
            protected Camera camera;
            protected Transform cameraPosition, targetPosition;
            protected Color color;
            protected RenderTexture renderTexture;
            protected int width, height;

            public void Initialize()
            {
                renderTexture.Release();
                if (camera)
                    camera.enabled = false;
                cameraPosition = null;
                targetPosition = null;
            }

            protected RenderTexture GetRenderTexture()
            {
                renderTexture.Create();
                return renderTexture;
            }

            protected void SetRenderTexture(float width_, float height_)
            {
                if (width_ < 1 || height_ < 1)
                    return;
                if (renderTexture.width == (int) width_ && renderTexture.height == (int) height_)
                    return;
                width = (int) width_;
                height = (int) height_;
                renderTexture = new RenderTexture(width, height, 0);
            }

            protected static Vector3 SmoothCamPos(Vector3 from, Vector3 to, float shakewidth, float smoothspeed)
            {
                var t = Time.time;
                var _x = (Mathf.PerlinNoise(0, t) - 0.5f) * shakewidth;
                var _y = (Mathf.PerlinNoise(t, t) - 0.5f) * shakewidth;
                var _z = (Mathf.PerlinNoise(t, 0) - 0.5f) * shakewidth;
                return new Vector3(
                    Mathf.SmoothStep(from.x, to.x + _x, smoothspeed),
                    Mathf.SmoothStep(from.y, to.y + _y, smoothspeed),
                    Mathf.SmoothStep(from.z, to.z + _z, smoothspeed)
                );
            }

            public abstract void Action();
            public abstract void Draw(State current, Rect rect);
        }

        private class PokoCam : IOCam
        {
            private const string vagina = "_IK_vagina", anal = "_IK_anal", mouth = "Mouth";
            private Vector3 offset;

            public PokoCam()
            {
                color = Color.white;
                renderTexture = new RenderTexture(1, 1, 0);
            }

            ~PokoCam()
            {
                Initialize();
                DestroyObject(camera.gameObject);
            }

            public bool SetupCam(Maid maid, Maid man, MuffType type)
            {
                if (!maid.IsValid() || !man.IsValid())
                    return false;

                if (camera == null)
                {
                    camera = new GameObject($"{PluginName}__Cam").AddComponent<Camera>();
                    camera.nearClipPlane = 0.01f;
                    camera.farClipPlane = 100f;
                }
                else
                {
                    camera.targetTexture = renderTexture;
                    camera.fieldOfView = settings.PokoCam_FOV;
                    camera.enabled = false;
                }

                cameraPosition = man.body0.trManChinko;

                targetPosition = type switch
                {
                    MuffType.Front => maid.body0.GetBone(vagina),
                    MuffType.Back => maid.body0.GetBone(anal),
                    MuffType.Mouth => maid.body0.GetBone(mouth),
                    _ => targetPosition
                };

                SetMask(man);

                return camera && cameraPosition && targetPosition;
            }

            private void SetMask(Maid man)
            {
                if (camera == null)
                    return;
                var mask = CreateMask();
                camera.cullingMask = ~mask;
                foreach (var m in man.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (m != null)
                        m.gameObject.layer = ManLayer;
            }

            private static int CreateMask()
            {
                var mask = 1 << Overlay.OverlayLayer;
                if (settings.PokoCam_HideManBody)
                    mask += 1 << ManLayer;
                if (settings.PokoCam_HideBG)
                {
                    bgObject = GameMain.Instance.BgMgr.current_bg_object;
                    mask += 1 << bgObject.layer;
                }

                return mask;
            }

            public override void Action()
            {
                offset = settings.PokoCam_Offset;
                var newpos = cameraPosition.position + offset;
                var pos = SmoothCamPos(camera.transform.localPosition, newpos,
                    settings.PokoCam_ShakeWidth, settings.PokoCam_SmoothSpeed);
                var up = cameraPosition.up;
                var rot = Quaternion.LookRotation(targetPosition.position - newpos,
                    settings.PokoCam_UpsideDown ? -up : up);
                camera.transform.SetPositionAndRotation(pos, rot);
                camera.enabled = true;
            }

            public override void Draw(State current, Rect rect)
            {
                var muffType = current.PrimaryMuff;
                if (muffType == MuffType.None)
                    return;
                if (current.PokoType != PokoType.TNP && muffType != MuffType.Mouth)
                    return;
                color.a = current.showSkillSelect || current.showConfigPanel || current.showResultPanel
                    ? 0.0f
                    : settings.PokoCam_Transparency * Mathf.Clamp01(current.rates[(int) current.PrimaryMuff] * 10);
                GUI.color = color;
                SetRenderTexture(rect.width, rect.height);
                GUI.DrawTexture(rect, GetRenderTexture(), ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
            }
        }

        private class FaceCam : IOCam
        {
            private const string FaceNub = "Face_nub";
            private const float FaceOffset = 0.008f;

            public FaceCam()
            {
                color = Color.white;
                renderTexture = new RenderTexture(1, 1, 0);
            }

            ~FaceCam()
            {
                Initialize();
                DestroyObject(camera.gameObject);
            }

            public bool SetupCam(Maid maid)
            {
                if (!maid.IsValid())
                    return false;

                if (camera == null)
                {
                    camera = new GameObject($"{PluginName}__faceCam").AddComponent<Camera>();
                    camera.nearClipPlane = 0.01f;
                    camera.farClipPlane = 100f;
                }
                else
                {
                    camera.targetTexture = renderTexture;
                    camera.fieldOfView = settings.FaceCam_FOV;
                    camera.enabled = false;
                }

                targetPosition = maid.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Head);
                cameraPosition = GetCameraPos(maid);
                bgObject = GameMain.Instance.BgMgr.current_bg_object;
                var mask = (1 << Overlay.OverlayLayer) + (1 << ManLayer) + (1 << bgObject.layer);
                if (camera != null)
                    camera.cullingMask = ~mask;

                return camera && cameraPosition && targetPosition;
            }

            private Transform GetCameraPos(Maid maid)
            {
                var head = maid.body0.goSlot[(int) TBody.SlotID.head].obj_tr;
                for (var j = 0; j < head.childCount; j++)
                {
                    var h = head.GetChild(j);
                    for (var i = 0; i < h.childCount; i++)
                    {
                        var f = h.GetChild(i);
                        if (f.name.Equals(FaceNub))
                            return f;
                    }
                }

                return null;
            }

            public override void Draw(State current, Rect rect)
            {
                if (current.rates[2] > 0.01f)
                    return;
                color.a = current.showSkillSelect || current.showConfigPanel || current.showResultPanel
                    ? 0.0f
                    : settings.FaceCam_Transparency;
                GUI.color = color;
                SetRenderTexture(rect.width, rect.height);
                GUI.DrawTexture(rect, GetRenderTexture(), ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
            }

            public override void Action()
            {
                var position = cameraPosition.position;
                var dir = targetPosition.position - position;
                var newpos = position - dir * settings.FaceCam_Distance;
                var pos = SmoothCamPos(position, newpos,
                    settings.FaceCam_ShakeWidth, settings.FaceCam_SmoothSpeed);
                var rot = Quaternion.LookRotation(dir + cameraPosition.up * FaceOffset, cameraPosition.up);
                camera.transform.SetPositionAndRotation(pos, rot);
                camera.enabled = true;
            }
        }

        #endregion


        public class PokoTrail
        {
            private GameObject obj;
            private Shader shader;
            private TrailRenderer trail;

            private bool SetUpTrail()
            {
                if (shader && trail)
                    return true;
                shader = Shader.Find(HiddenShader);

                if (shader == null)
                    return false;

                if (trail == null)
                {
                    trail = new GameObject($"{PluginName}__trail__").AddComponent<TrailRenderer>();
                    obj = trail.gameObject;
                    trail.material.shader = shader;
                    trail.material.SetInt("_ZTest", (int) CompareFunction.Always);
                    trail.startWidth = 0.01f;
                    trail.endWidth = 0.0f;
                    trail.time = 2;
                    trail.minVertexDistance = 0.001f;
                    trail.alignment = LineAlignment.View;
                    trail.enabled = false;
                }

                return shader && trail;
            }

            public void Draw(Vector3 pos, Color startcolor, Color endcolor)
            {
                if (!SetUpTrail())
                    return;
                trail.transform.localPosition = pos;
                trail.startColor = startcolor;
                trail.endColor = endcolor;
                trail.enabled = true;
            }

            ~PokoTrail()
            {
                DestroyObject(trail);
                DestroyObject(obj);
            }
        }

        public class Settings
        {
            private static Settings _instance;
            private static FileInfo fileInfo;
            public float Anim_Speed = 0.5f;
            public KeyCode ControllerHotKey = KeyCode.U;

            //FaceCam
            public bool enableFaceCam;

            //Morpher
            public bool enableMorpher;

            //FlipAnim
            public bool enableOnGUI;

            //Overlay
            public bool enableOverlay;

            public bool enablePlugin;

            //PokoCam
            public bool enablePokoCam;
            public float FaceCam_Distance = 20.0f;
            public float FaceCam_FOV = 20.0f;
            public float FaceCam_ShakeWidth = 0.01f;
            public float FaceCam_SmoothSpeed = 0.3f;
            public float FaceCam_Transparency = 1.0f;
            public bool Morpher_EnableFix = true;
            public string Morpher_KeyA = "analkupa";
            public string Morpher_KeyV = "kupa";
            public float Morpher_Max = 0.80f;
            public float Morpher_Min = 0.05f;
            public float Morpher_Threshold = 0.3f;
            public float Morpher_Waitframe = 5.0f;

            public bool OnGUI_HidePenis;
            public float OnGUI_offset = 5.0f;
            public float OnGUI_transparency = 1.0f;
            public float OnGUI_x_offset;
            public float OnGUI_x_scale = 1.0f;
            public float OnGUI_y_offset;
            public float OnGUI_y_scale = 1.0f;
            public float Overlay_CameraDistance = 0.25f;

            public float Overlay_DirectionalOffset;
            public float Overlay_LineScale;
            public float Overlay_LineWidth = 1.0f;
            public float Overlay_RenderQueue = 4000f;

            public float Overlay_Transparency = 0.7f;

            //internal Rect PokoCam_Pos = Rect.zero;
            //internal bool PokoCam_CustomPos = false;
            public float PokoCam_FOV = 90.0f;
            public bool PokoCam_HideBG = false;
            public bool PokoCam_HideManBody = true;
            public Vector3 PokoCam_Offset = Vector3.zero;
            public float PokoCam_ShakeWidth = 0.03f;
            public float PokoCam_SmoothSpeed = 0.3f;
            public float PokoCam_Transparency = 1.0f;
            public bool PokoCam_UpsideDown;

            private Settings()
            {
            }

            private static Settings Instance => _instance ?? (_instance = new Settings());

            public static void Save()
            {
                if (fileInfo == null)
                    return;

                using (var stream = fileInfo.Open(FileMode.Create))
                {
                    var serializer = new XmlSerializer(typeof(Settings));
                    serializer.Serialize(stream, Instance);
                }
            }

            public static Settings Load()
            {
                return Load(fileInfo);
            }

            public static Settings Load(FileInfo fileInfo)
            {
                Settings.fileInfo = fileInfo;
                if (!fileInfo.Exists)
                {
                    using (fileInfo.Create())
                    {
                    }

                    Save();
                }

                using (var stream = fileInfo.Open(FileMode.Open))
                {
                    var serializer = new XmlSerializer(typeof(Settings));
                    var reader = XmlReader.Create(stream, new XmlReaderSettings());
                    return _instance = (Settings) serializer.Deserialize(reader);
                }
            }

            public static Settings GetDef()
            {
                var _ = new Settings();
                return ReferenceEquals(_, _instance) ? null : _;
            }

            public void OpenFile()
            {
                if (!fileInfo.Exists)
                    return;
                var ps = new ProcessStartInfo
                {
                    FileName = "notepad",
                    Arguments = fileInfo.FullName
                };
                using (var process = Process.Start(ps))
                {
                }

                ;
            }
        }

        private class Controller
        {
            private const string MuffNames = "前後口";
            private const string TargetMaid = "対象メイド";
            private const string PokoNames = "棒振張指舌";
            private static Rect winRect;
            private static readonly Color bgColor = Color.white, onColor = Color.green, offColor = Color.black;
            private static readonly GUILayoutOption[] noOptions = new GUILayoutOption[0];
            private static readonly GUILayoutOption noExpandWidth = ExpandWidth(false);
            private static GUILayoutOption width010, width015, width020, width025;
            private readonly Dictionary<string, Container> containers;

            private readonly FlipAnimData[] fAData;
            private readonly InOutAnimation plugin;
            private readonly ToggleButton pluginEnabler;
            private bool _show;
            private Vector2 winScrollRect;

            public Controller(InOutAnimation plugin)
            {
                this.plugin = plugin;
                var s = settings;
                var def = Settings.GetDef();
                fAData = new[]
                {
                    new FlipAnimData($"{MuffNames[0]}", plugin.flipAnims[0]),
                    new FlipAnimData($"{MuffNames[1]}", plugin.flipAnims[1]),
                    new FlipAnimData($"{MuffNames[2]}", plugin.flipAnims[2])
                };
                pluginEnabler = new ToggleButton("プラグイン有効", s.enablePlugin, b => settings.enablePlugin = b);
                containers = new Dictionary<string, Container>
                {
                    {"ongui", new Container("OnGUI表示")},
                    {"overlay", new Container("メイドに重ねて表示")},
                    {"morpher", new Container("シェイプキー連動")},
                    {"pokocam", new Container("股間視点")},
                    {"facecam", new Container("顔カメラ")}
                };

                containers["ongui"].Add(new ToggleButton("有効", s.enableOnGUI, b => settings.enableOnGUI = b));
                containers["ongui"].Add(new LabelSlider("間隔", s.OnGUI_offset, -100.0f, 100.0f, def.OnGUI_offset,
                    f => settings.OnGUI_offset = f));
                containers["ongui"].Add(new LabelSlider("横スケール", s.OnGUI_x_scale, 0.0f, 2.0f, def.OnGUI_x_scale,
                    f => settings.OnGUI_x_scale = f));
                containers["ongui"].Add(new LabelSlider("縦スケール", s.OnGUI_y_scale, 0.0f, 2.0f, def.OnGUI_y_scale,
                    f => settings.OnGUI_y_scale = f));
                containers["ongui"].Add(new LabelSlider("横オフセット", s.OnGUI_x_offset, -600.0f, 300.0f, def.OnGUI_x_offset,
                    f => settings.OnGUI_x_offset = f));
                containers["ongui"].Add(new LabelSlider("縦オフセット", s.OnGUI_y_offset, -500.0f, 500.0f, def.OnGUI_y_offset,
                    f => settings.OnGUI_y_offset = f));
                containers["ongui"].Add(new LabelSlider("透明度", s.OnGUI_transparency, 0.0f, 1.0f, def.OnGUI_transparency,
                    f => settings.OnGUI_transparency = f));
                containers["ongui"]
                    .Add(new ToggleButton("Hide man penis", s.OnGUI_HidePenis, b => settings.OnGUI_HidePenis = b));


                containers["overlay"].Add(new ToggleButton("有効", s.enableOverlay, b => settings.enableOverlay = b));
                containers["overlay"].Add(new LabelSlider("太さ", s.Overlay_LineWidth, 0.0f, 2.0f, def.Overlay_LineWidth,
                    f => settings.Overlay_LineWidth = f));
                containers["overlay"].Add(new LabelSlider("拡大", s.Overlay_LineScale, -0.5f, 0.5f, def.Overlay_LineScale,
                    f => settings.Overlay_LineScale = f));
                containers["overlay"].Add(new LabelSlider("透明度", s.Overlay_Transparency, 0.0f, 1.0f,
                    def.Overlay_Transparency, f => settings.Overlay_Transparency = f));
                containers["overlay"].Add(new LabelSlider("カメラ距離", s.Overlay_CameraDistance, 0.0f, 1.0f,
                    def.Overlay_CameraDistance, f => settings.Overlay_CameraDistance = f));
                containers["overlay"].Add(new LabelSlider("Dir. Offset.", s.Overlay_DirectionalOffset, -0.1f, 0.1f,
                    def.Overlay_DirectionalOffset, f => settings.Overlay_DirectionalOffset = f));
                containers["overlay"].Add(new LabelSlider("Overlay renderqueue", s.Overlay_RenderQueue, 1000, 5000,
                    def.Overlay_RenderQueue, f => settings.Overlay_RenderQueue = f, true));

                containers["morpher"].Add(new ToggleButton("有効", s.enableMorpher, b => settings.enableMorpher = b));
                containers["morpher"].Add(new ShapeKeyChange("前穴用キー", s.Morpher_KeyV, n => settings.Morpher_KeyV = n));
                containers["morpher"].Add(new ShapeKeyChange("後穴用キー", s.Morpher_KeyA, n => settings.Morpher_KeyA = n));
                containers["morpher"].Add(new LabelSlider("閾値", s.Morpher_Threshold, 0.0f, 1.0f, def.Morpher_Threshold,
                    f => settings.Morpher_Threshold = f));
                containers["morpher"].Add(new LabelSlider("最小値", s.Morpher_Min, 0.0f, 1.0f, def.Morpher_Min,
                    f => settings.Morpher_Min = f));
                containers["morpher"].Add(new LabelSlider("最大値", s.Morpher_Max, 0.0f, 1.0f, def.Morpher_Max,
                    f => settings.Morpher_Max = f));
                var wf = new LabelSlider("更新フレーム", s.Morpher_Waitframe, 1, 60, def.Morpher_Waitframe,
                    f => settings.Morpher_Waitframe = f, true);
                containers["morpher"].Add(new EnableToggle("FixBlendValues", s.Morpher_EnableFix, wf,
                    b => settings.Morpher_EnableFix = b));

                containers["pokocam"].Add(new ToggleButton("有効", s.enablePokoCam, b => settings.enablePokoCam = b));
                var st1 = new SimpleToggle("男を隠す", s.PokoCam_HideManBody, b => settings.PokoCam_HideManBody = b);
                //                var st2 = new SimpleToggle("背景を隠す", s.PokoCam_HideBG, b => settings.PokoCam_HideBG = b);
                var st3 = new SimpleToggle("上下逆", s.PokoCam_UpsideDown, b => settings.PokoCam_UpsideDown = b);
                containers["pokocam"].Add(new HorizontalGroup(st1, st3));
                containers["pokocam"].Add(new LabelSlider("視野", s.PokoCam_FOV, 15, 180, def.PokoCam_FOV,
                    f => settings.PokoCam_FOV = f, true));
                containers["pokocam"].Add(new LabelSlider("透明度", s.PokoCam_Transparency, 0.0f, 1.0f,
                    def.PokoCam_Transparency, f => settings.PokoCam_Transparency = f));
                containers["pokocam"].Add(new Vec3Slider("オフセット", s.PokoCam_Offset, def.PokoCam_Offset, -0.2f, 0.2f,
                    v => settings.PokoCam_Offset = v));

                containers["facecam"].Add(new ToggleButton("有効", s.enableFaceCam, b => settings.enableFaceCam = b));
                containers["facecam"].Add(new LabelSlider("距離", s.FaceCam_Distance, 5.0f, 40.0f, def.FaceCam_Distance,
                    f => settings.FaceCam_Distance = f));
                containers["facecam"].Add(new LabelSlider("視野", s.FaceCam_FOV, 15, 50, def.FaceCam_FOV,
                    f => settings.FaceCam_FOV = f, true));
                containers["facecam"].Add(new LabelSlider("透明度", s.FaceCam_Transparency, 0.0f, 1.0f,
                    def.FaceCam_Transparency, f => settings.FaceCam_Transparency = f));
            }

            public bool showController
            {
                get => _show;
                set
                {
                    var mouse = Event.current.mousePosition;
                    winRect.Set(
                        Mathf.Clamp(mouse.x - 10.0f, 0.0f, Screen.width - Screen.width * 0.25f),
                        Mathf.Clamp(mouse.y - 10.0f, 0.0f, Screen.height - 400),
                        Screen.width * 0.25f,
                        Screen.height * 0.6f);
                    _show = value;
                    GC.Collect();
                }
            }

            public void Draw()
            {
                winRect = Window(plugin.windowId, winRect, WindowFunc, "", noOptions);
            }

            private void WindowFunc(int id)
            {
                var stg = settings;

                winScrollRect = BeginScrollView(winScrollRect, false, false, noOptions);
                pluginEnabler.Draw();
                //if (Button("設定ファイルを開く", ExpandWidth(false))) settings.OpenFile();

                if (stg.enablePlugin && plugin.mediator.TargetMaid != null)
                {
                    BeginHorizontal(noOptions);
                    {
                        Label(TargetMaid, noExpandWidth);
                        Box(plugin.current.isPlay ? plugin.mediator.TargetMaid.status.fullNameJpStyle : "", noOptions);
                        plugin.mediator.SwapTargetNPC = plugin.current.PlayMode == PlayMode.Swap
                                                        && Toggle(plugin.mediator.SwapTargetNPC, "NPC", noExpandWidth);
                    }
                    EndHorizontal();

                    foreach (var fA in fAData)
                        fA.Draw();

                    for (var i = 0; i < plugin.mediator.pokos.Length; i++)
                    {
                        var p = plugin.mediator.pokos[i];
                        if (p == null || !p.Validated)
                            continue;
                        BeginHorizontal(noOptions);
                        {
                            Box($"{i} {GetPokoTypeChar(p)} : {MuffNames[(int) p.TargetMuff]}", noOptions);
                        }
                        EndHorizontal();
                    }

                    foreach (var container in containers.Values)
                        container.Draw();
                }

                EndScrollView();

                GUI.DragWindow();
            }

            private char GetPokoTypeChar(Poko type)
            {
                return type switch
                {
                    TNP _ => PokoNames[0],
                    Vibe _ => PokoNames[1],
                    Dildo _ => PokoNames[2],
                    Finger _ => PokoNames[3],
                    Tongue _ => PokoNames[4],
                    _ => '_'
                };
            }

            private class Container
            {
                private const string Down = "▼";
                private readonly List<IDrawable> contents;
                private readonly string label;
                private bool visible;

                public Container(string label)
                {
                    this.label = label;
                    contents = new List<IDrawable>();
                }

                public void Add(IDrawable controllerParts)
                {
                    contents.Add(controllerParts);
                }

                public void Draw()
                {
                    BeginHorizontal(noOptions);
                    {
                        Box(label, noOptions);
                        if (Button(Down, noExpandWidth))
                            visible = !visible;
                    }
                    EndHorizontal();

                    if (!visible)
                        return;
                    foreach (var drawable in contents)
                        drawable.Draw();
                }
            }

            private class HorizontalGroup : IDrawable
            {
                private readonly List<IDrawable> contents;

                public HorizontalGroup(params IDrawable[] drawable)
                {
                    contents = drawable.ToList();
                }

                public void Draw()
                {
                    BeginHorizontal(noOptions);
                    foreach (var drawable in contents) drawable.Draw();
                    EndHorizontal();
                }

                public void Add(IDrawable drawable)
                {
                    contents.Add(drawable);
                }
            }

            private class FlipAnimData : IDrawable
            {
                private const string Reload = "再読込",
                    mes1 = "準備完了",
                    mes2 = "ex画像がありません",
                    mes3 = "画像がありません";

                private static Color color;
                private readonly FlipAnim flipAnim;
                private readonly string label;
                private string message;

                public FlipAnimData(string label, FlipAnim flipAnim)
                {
                    this.label = label;
                    this.flipAnim = flipAnim;
                }

                public void Draw()
                {
                    CheckLoaded();

                    BeginHorizontal(noOptions);
                    {
                        width010 = Width(winRect.width * 0.10f);
                        width015 = Width(winRect.width * 0.15f);
                        width020 = Width(winRect.width * 0.20f);

                        Label(label, width010);
                        Box($"{flipAnim.CurrentFrame:D2}/{flipAnim.MaxFrame:D2}", width020);
                        GUI.contentColor = color;
                        Box($"{message}", noOptions);
                        GUI.contentColor = bgColor;
                        if (Button(Reload, width015))
                            flipAnim.Load();
                    }
                    EndHorizontal();
                }

                private void CheckLoaded()
                {
                    if (flipAnim.TextureLoaded)
                    {
                        if (flipAnim.TextureLoadedEx)
                        {
                            color = Color.white;
                            message = mes1;
                            return;
                        }

                        color = Color.yellow;
                        message = mes2;
                        return;
                    }

                    color = Color.magenta;
                    message = mes3;
                }
            }

            private class SimpleToggle : IDrawable
            {
                private readonly string label;
                private readonly Action<bool> onChanged;
                private bool recent;
                private bool value;

                public SimpleToggle(string label, bool value, Action<bool> onChanged)
                {
                    this.label = label;
                    this.value = value;
                    recent = this.value;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    value = Toggle(value, label, noOptions);
                    if (recent == value)
                        return;
                    onChanged(value);
                    recent = value;
                }
            }

            private class Vec3Slider : IDrawable
            {
                private readonly Vector3 def;
                private readonly string label;
                private readonly float min, max;
                private readonly Action<Vector3> onChanged;
                private Vector3 value, recent;

                public Vec3Slider(string label, Vector3 value, Vector3 def, float min, float max,
                    Action<Vector3> onChanged)
                {
                    this.label = label;
                    this.value = value;
                    recent = this.value;
                    this.def = def;
                    this.min = min;
                    this.max = max;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    BeginHorizontal(noOptions);
                    {
                        Label(label, noOptions);
                        if (Button("def", noExpandWidth))
                            value = def;
                    }
                    EndHorizontal();
                    BeginHorizontal(noOptions);
                    {
                        Label("X", noExpandWidth);
                        value.x = HorizontalSlider(value.x, min, max, noOptions);
                        Label("Y", noExpandWidth);
                        value.y = HorizontalSlider(value.y, min, max, noOptions);
                        Label("Z", noExpandWidth);
                        value.z = HorizontalSlider(value.z, min, max, noOptions);
                    }
                    EndHorizontal();
                    if (recent.Equals(value))
                        return;
                    onChanged(value);
                    recent = value;
                }
            }

            private class EnableToggle : IDrawable
            {
                private readonly string label;
                private readonly Action<bool> onChanged;
                private readonly IDrawable parts;
                private bool enable, recent;

                public EnableToggle(string label, bool enable, IDrawable parts, Action<bool> onChanged)
                {
                    this.label = label;
                    this.enable = enable;
                    this.parts = parts;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    enable = Toggle(enable, label, noOptions);
                    GUI.enabled = enable;
                    parts.Draw();
                    GUI.enabled = true;

                    if (recent == enable)
                        return;
                    onChanged(enable);
                    recent = enable;
                }
            }

            private class ShapeKeyChange : IDrawable
            {
                private const string Change = "変更";
                private readonly string label;
                private readonly Action<string> onChanged;
                private bool Editable;
                private string key, recent;

                public ShapeKeyChange(string label, string key, Action<string> onChanged)
                {
                    this.label = label;
                    this.key = key;
                    recent = key;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    width025 = Width(winRect.width * 0.25f);

                    BeginHorizontal(noOptions);
                    Label(label, width025);
                    if (Editable)
                        try
                        {
                            key = TextArea($"{key}", noOptions).Trim();
                        }
                        catch (FormatException e)
                        {
                            Console.WriteLine(e);
                            key = "";
                        }
                    else
                        Box($"{key}", noOptions);

                    GUI.backgroundColor = Editable ? onColor : offColor;
                    if (Button(Change, noExpandWidth))
                        Editable = !Editable;
                    GUI.backgroundColor = bgColor;
                    EndHorizontal();

                    if (recent.Equals(key))
                        return;
                    onChanged(key);
                    recent = key;
                }
            }

            private class ToggleButton : IDrawable
            {
                private readonly string label;
                private readonly Action<bool> onChanged;
                private bool value, recent;

                public ToggleButton(string label, bool value, Action<bool> onChanged)
                {
                    this.label = label;
                    this.value = value;
                    recent = value;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    GUI.backgroundColor = value ? onColor : offColor;
                    if (Button(label, noOptions))
                        value = !value;
                    GUI.backgroundColor = bgColor;

                    if (recent == value)
                        return;
                    onChanged(value);
                    recent = value;
                }
            }

            private class LabelSlider : IDrawable
            {
                private const string _def = "def";
                private readonly bool ceil;
                private readonly string label;
                private readonly float min, max, def;
                private readonly Action<float> onChanged;
                private string str;
                private float value, recent;

                public LabelSlider(string label, float value, float min, float max, float def, Action<float> onChanged,
                    bool ceil = false)
                {
                    this.label = label;
                    this.value = value;
                    recent = this.value;
                    this.min = min;
                    this.max = max;
                    this.def = def;
                    this.ceil = ceil;
                    this.onChanged = onChanged;
                }

                public void Draw()
                {
                    width015 = Width(winRect.width * 0.15f);
                    width025 = Width(winRect.width * 0.25f);

                    BeginHorizontal(noOptions);
                    {
                        if (ceil)
                            value = Mathf.Ceil(value);
                        str = ceil ? $"{value}" : $"{value:F2}";
                        Label(label, width025);
                        Box(str, width015);
                        value = HorizontalSlider(value, min, max, noOptions);
                        if (Button(_def, noExpandWidth))
                            value = def;
                    }
                    EndHorizontal();

                    if (recent.Equals(value))
                        return;
                    onChanged(value);
                    recent = value;
                }
            }

            private interface IDrawable
            {
                void Draw();
            }
        }

        private class MessageBox
        {
            private readonly Rect winRect = new Rect(30, Screen.height * 0.5f, 400, 300);
            private GUIContent message;

            public void Draw(GUIContent content)
            {
                message = content;
                BeginArea(winRect);
                {
                    Box(message);
                }
                EndArea();
            }
        }

        #endregion
    }


    public static class Util
    {
        public static bool IsValid(this Maid maid)
        {
            return maid != null && maid.isActiveAndEnabled &&
                   !maid.IsAllProcPropBusy && maid.Visible &&
                   !string.IsNullOrEmpty(maid.body0.LastAnimeFN);
        }

        public static IEnumerable<Maid> GetMaidCol(this CharacterMgr mgr, Func<Maid, bool> pred)
        {
            return Enumerable.Range(0, mgr.GetMaidCount()).Select(mgr.GetMaid).Where(pred);
        }
    }

    #region enums

    internal enum MuffNum
    {
        Unknown,
        Double,
        Triple
    }

    internal enum MuffType
    {
        Front,
        Back,
        Mouth,
        None
    }

    internal enum PlayMode
    {
        Normal,
        Multiple,
        Swap,
        Harem,
        Self
    }

    internal enum sPosition
    {
        None,
        Normal,
        Dog,
        Side
    }

    internal enum PlayState
    {
        None,
        Insert,
        Eject,
        Wait,
        Play,
        Shot,
        ShotFin
    }

    internal enum PokoType
    {
        TNP,
        Vibe,
        Dildo,
        Finger,
        Tongue
    }

    #endregion
}