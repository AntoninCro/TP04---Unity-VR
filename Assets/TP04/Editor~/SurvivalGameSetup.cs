using System.IO;
using TMPro;
using TP04.Survival;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace TP04.Survival.EditorTools
{
    /// <summary>
    /// Builds the whole survival mode into the open scene: prefabs (robot, bolt, shield),
    /// the world space menu, the spawner and every reference between them.
    /// Safe to run again, it replaces what it created the previous time.
    /// </summary>
    public static class SurvivalGameSetup
    {
        const string k_Prefabs = "Assets/TP04/Prefabs";
        const string k_Materials = "Assets/TP04/Materials";
        const string k_CharacterPrefab = "Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab";
        const string k_Textures = "Assets/SciFiWarriorPBRHPPolyart/Textures";

        const float k_TargetRobotHeight = 1.8f;

        [MenuItem("TP04/Créer le mode Survie", false, 0)]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("Mode Survie", "Ouvre d'abord une scène.", "OK");
                return;
            }

            EnsureFolder(k_Prefabs);
            EnsureFolder(k_Materials);

            GameObject boltPrefab = BuildBoltPrefab();
            GameObject robotPrefab = BuildRobotPrefab(boltPrefab);
            GameObject shieldPrefab = BuildShieldPrefab();

            XROrigin origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("Mode Survie",
                    "Aucun XR Origin dans la scène. Ajoute le rig VR avant de lancer le montage.", "OK");
                return;
            }

            EnsureEventSystem();

            PlayerHealth health = EnsurePlayerHealth(origin);
            EnsureHeadHitbox(origin);

            GameObject survival = Replace("Survival");
            var manager = survival.AddComponent<SurvivalGameManager>();
            var spawner = survival.AddComponent<EnemySpawner>();

            Transform player = origin.Camera != null ? origin.Camera.transform : origin.transform;

            SetRef(spawner, "m_RobotPrefab", robotPrefab);
            SetRef(spawner, "m_Player", player);
            SetFloat(spawner, "m_GroundY", origin.transform.position.y);

            SetRef(manager, "m_Spawner", spawner);
            SetRef(manager, "m_PlayerHealth", health);

            SurvivalUI ui = BuildCanvas(origin);

            PlaceShield(shieldPrefab, origin);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = survival;

            Debug.Log("[TP04] Mode Survie monté : robot, boulon, bouclier, menu et spawner sont en place. " +
                      "Lance le Play, choisis un angle puis appuie sur DÉMARRER.", survival);

            if (ui == null)
                Debug.LogWarning("[TP04] Le canvas n'a pas pu être construit, vérifie que TextMeshPro est importé.");
        }

        // ------------------------------------------------------------------ prefabs --

        static GameObject BuildBoltPrefab()
        {
            Material material = CreateMaterial("Enemy Bolt", mat =>
            {
                mat.SetColor("_BaseColor", new Color(1f, 0.35f, 0.15f));
                mat.SetFloat("_Smoothness", 0.6f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", new Color(3f, 0.9f, 0.3f));
            });

            var root = new GameObject("EnemyBolt");

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            // The bolt sweeps a sphere itself, it must not carry a physics collider.
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(0.09f, 0.09f, 0.22f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = material;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 2.5f;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;

            root.AddComponent<EnemyProjectile>();

            return SaveAndDiscard(root, $"{k_Prefabs}/EnemyBolt.prefab");
        }

        static GameObject BuildRobotPrefab(GameObject boltPrefab)
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterPrefab);
            if (character == null)
            {
                Debug.LogError($"[TP04] Prefab du robot introuvable : {k_CharacterPrefab}");
                return null;
            }

            var root = new GameObject("Robot");

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(character);
            // Unpacked so the materials can be fixed directly instead of piling up overrides.
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);

            Material robotMaterial = CreateRobotMaterial();
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = robotMaterial;
                renderer.sharedMaterials = materials;
            }

            // A SkinnedMeshRenderer reports the bounds of its bind pose, which can be loose.
            // So the model is only rescaled on a gross import error (a factor of ten or more),
            // never on a plausible height: guessing wrong here would give a giant or a dwarf.
            float measured = MeasureHeight(visual);
            float height = k_TargetRobotHeight;

            if (measured >= 0.3f && measured <= 10f)
            {
                height = measured;
                Debug.Log($"[TP04] Robot mesuré à {height:0.00} m, échelle du modèle laissée telle quelle.");
            }
            else if (measured > 0.0001f)
            {
                float scale = k_TargetRobotHeight / measured;
                visual.transform.localScale = Vector3.one * scale;
                Debug.LogWarning($"[TP04] Robot mesuré à {measured:0.000} m : échelle d'import aberrante, " +
                                 $"corrigée en x{scale:0.000} pour faire {k_TargetRobotHeight} m. " +
                                 "Vérifie le rendu et ajuste le Scale du prefab si besoin.");
            }

            var body = root.AddComponent<CapsuleCollider>();
            body.height = height;
            body.radius = Mathf.Clamp(height * 0.18f, 0.15f, 0.5f);
            body.center = new Vector3(0f, height * 0.5f, 0f);

            var rigidbody = root.AddComponent<Rigidbody>();
            // The AI drives the transform, physics only needs to know the collider moves.
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            var firePoint = new GameObject("FirePoint");
            firePoint.transform.SetParent(root.transform, false);
            firePoint.transform.localPosition = new Vector3(0.15f, height * 0.78f, 0.3f);

            var enemy = root.AddComponent<RobotEnemy>();
            SetRef(enemy, "m_ProjectilePrefab", boltPrefab);
            SetRef(enemy, "m_FirePoint", firePoint.transform);

            return SaveAndDiscard(root, $"{k_Prefabs}/Robot.prefab");
        }

        static GameObject BuildShieldPrefab()
        {
            Material material = CreateMaterial("Shield", mat =>
            {
                mat.SetColor("_BaseColor", new Color(0.24f, 0.55f, 0.85f));
                mat.SetFloat("_Metallic", 0.7f);
                mat.SetFloat("_Smoothness", 0.65f);
            });

            var root = new GameObject("Shield");

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            // Unity's cylinder points up, tip it over so the flat face looks forward.
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(0.46f, 0.02f, 0.46f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = material;

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "Handle";
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(root.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0f, -0.07f);
            handle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            handle.transform.localScale = new Vector3(0.025f, 0.06f, 0.025f);
            handle.GetComponent<MeshRenderer>().sharedMaterial = material;

            // A box rather than the primitive's capsule: a flattened capsule is a blob and the
            // bolts would appear to bounce off thin air.
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.46f, 0.46f, 0.06f);
            collider.center = Vector3.zero;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 1.5f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grip = new GameObject("Grip (Attach)");
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, -0.09f);

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip.transform;
            grab.useDynamicAttach = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.attachEaseInTime = 0f;
            grab.farAttachMode = InteractableFarAttachMode.Near;

            root.AddComponent<ShieldBlocker>();

            return SaveAndDiscard(root, $"{k_Prefabs}/Shield.prefab");
        }

        // -------------------------------------------------------------------- scène --

        static void EnsureEventSystem()
        {
            var existing = Object.FindAnyObjectByType<EventSystem>();
            GameObject go = existing != null ? existing.gameObject : new GameObject("EventSystem");

            if (existing == null)
                go.AddComponent<EventSystem>();

            // XR rays go through XRUIInputModule, any other module would fight with it.
            foreach (BaseInputModule module in go.GetComponents<BaseInputModule>())
            {
                if (!(module is XRUIInputModule))
                    Object.DestroyImmediate(module);
            }

            if (go.GetComponent<XRUIInputModule>() == null)
                go.AddComponent<XRUIInputModule>();
        }

        static PlayerHealth EnsurePlayerHealth(XROrigin origin)
        {
            var health = origin.GetComponent<PlayerHealth>();
            if (health == null)
                health = origin.gameObject.AddComponent<PlayerHealth>();

            return health;
        }

        static void EnsureHeadHitbox(XROrigin origin)
        {
            if (origin.Camera == null)
                return;

            Transform existing = origin.Camera.transform.Find("Head Hitbox");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var hitbox = new GameObject("Head Hitbox");
            hitbox.transform.SetParent(origin.Camera.transform, false);

            var sphere = hitbox.AddComponent<SphereCollider>();
            sphere.radius = 0.22f;
            // A trigger so it never blocks the player's own bullets or the interactors.
            sphere.isTrigger = true;
        }

        static void PlaceShield(GameObject shieldPrefab, XROrigin origin)
        {
            if (shieldPrefab == null)
                return;

            GameObject existing = GameObject.Find("Shield");
            if (existing != null && PrefabUtility.IsAnyPrefabInstanceRoot(existing))
                Object.DestroyImmediate(existing);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(shieldPrefab);
            Transform reference = origin.Camera != null ? origin.Camera.transform : origin.transform;

            Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            instance.transform.position = origin.transform.position
                                          + forward * 0.6f
                                          - right * 0.45f
                                          + Vector3.up * 0.9f;
        }

        // ----------------------------------------------------------------------- UI --

        static SurvivalUI BuildCanvas(XROrigin origin)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                Debug.LogError("[TP04] Aucune police TextMeshPro par défaut. Window > TextMeshPro > Import TMP Essential Resources.");
                return null;
            }

            GameObject canvasGo = Replace("Survival UI", typeof(RectTransform));

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>();
            // The XR ray needs this raycaster, the classic GraphicRaycaster ignores it.
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000f, 700f);
            rect.localScale = Vector3.one * 0.0016f;

            Transform reference = origin.Camera != null ? origin.Camera.transform : origin.transform;
            Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            rect.position = origin.transform.position + forward * 2f + Vector3.up * 1.5f;
            rect.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var ui = canvasGo.AddComponent<SurvivalUI>();

            // ---- menu
            GameObject menu = Panel(rect, "Menu", new Vector2(1000f, 700f));
            Label(menu.transform, "Titre", "SURVIE", font, 84, new Vector2(0f, 240f), new Vector2(900f, 120f));
            Label(menu.transform, "Consigne", "Angle d'apparition des robots", font, 40, new Vector2(0f, 130f), new Vector2(900f, 60f));

            Button arc90 = ArcButton(menu.transform, "Arc 90", "90°", font, new Vector2(-260f, 30f));
            Button arc180 = ArcButton(menu.transform, "Arc 180", "180°", font, new Vector2(0f, 30f));
            Button arc360 = ArcButton(menu.transform, "Arc 360", "360°", font, new Vector2(260f, 30f));

            Button start = BigButton(menu.transform, "Start", "DÉMARRER", font,
                new Vector2(0f, -140f), new Vector2(520f, 120f), new Color(0.13f, 0.55f, 0.26f));

            TextMeshProUGUI best = Label(menu.transform, "Record", "", font, 34, new Vector2(0f, -250f), new Vector2(900f, 60f));

            // ---- HUD
            GameObject hud = Panel(rect, "HUD", new Vector2(1000f, 700f), transparent: true);
            Label(hud.transform, "TempsTitre", "TEMPS", font, 32, new Vector2(-330f, 280f), new Vector2(300f, 50f));
            TextMeshProUGUI time = Label(hud.transform, "Temps", "00:00", font, 72, new Vector2(-330f, 210f), new Vector2(300f, 90f));
            Label(hud.transform, "ViesTitre", "VIES", font, 32, new Vector2(0f, 280f), new Vector2(300f, 50f));
            TextMeshProUGUI lives = Label(hud.transform, "Vies", "5 / 5", font, 72, new Vector2(0f, 210f), new Vector2(300f, 90f));
            Label(hud.transform, "RobotsTitre", "ROBOTS", font, 32, new Vector2(330f, 280f), new Vector2(300f, 50f));
            TextMeshProUGUI kills = Label(hud.transform, "Robots", "x0", font, 72, new Vector2(330f, 210f), new Vector2(300f, 90f));

            // ---- game over
            GameObject over = Panel(rect, "GameOver", new Vector2(1000f, 700f));
            Label(over.transform, "Titre", "PARTIE TERMINÉE", font, 72, new Vector2(0f, 230f), new Vector2(900f, 110f));
            TextMeshProUGUI result = Label(over.transform, "Resultat", "", font, 44, new Vector2(0f, 60f), new Vector2(900f, 240f));
            Button restart = BigButton(over.transform, "Rejouer", "REJOUER", font,
                new Vector2(-150f, -180f), new Vector2(400f, 110f), new Color(0.13f, 0.55f, 0.26f));
            Button back = BigButton(over.transform, "Menu", "MENU", font,
                new Vector2(200f, -180f), new Vector2(280f, 110f), new Color(0.25f, 0.27f, 0.32f));

            SetRef(ui, "m_MenuPanel", menu);
            SetRef(ui, "m_HudPanel", hud);
            SetRef(ui, "m_GameOverPanel", over);
            SetRef(ui, "m_StartButton", start);
            SetRef(ui, "m_Arc90Button", arc90);
            SetRef(ui, "m_Arc180Button", arc180);
            SetRef(ui, "m_Arc360Button", arc360);
            SetRef(ui, "m_BestTimeLabel", best);
            SetRef(ui, "m_TimeLabel", time);
            SetRef(ui, "m_LivesLabel", lives);
            SetRef(ui, "m_KillsLabel", kills);
            SetRef(ui, "m_ResultLabel", result);
            SetRef(ui, "m_RestartButton", restart);
            SetRef(ui, "m_BackToMenuButton", back);

            return ui;
        }

        static GameObject Panel(Transform parent, string name, Vector2 size, bool transparent = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var image = go.GetComponent<Image>();
            image.color = transparent ? new Color(0f, 0f, 0f, 0.35f) : new Color(0.07f, 0.08f, 0.11f, 0.92f);
            image.raycastTarget = !transparent;

            return go;
        }

        static TextMeshProUGUI Label(Transform parent, string name, string text, TMP_FontAsset font,
            float size, Vector2 position, Vector2 area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = area;

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            return label;
        }

        static Button ArcButton(Transform parent, string name, string text, TMP_FontAsset font, Vector2 position)
        {
            return BigButton(parent, name, text, font, position, new Vector2(220f, 110f), new Color(0.18f, 0.20f, 0.24f));
        }

        static Button BigButton(Transform parent, string name, string text, TMP_FontAsset font,
            Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            Label(go.transform, "Label", text, font, size.y * 0.38f, Vector2.zero, size);

            return button;
        }

        // ------------------------------------------------------------------ helpers --

        static Material CreateRobotMaterial()
        {
            return CreateMaterial("Robot URP", mat =>
            {
                Texture albedo = LoadTexture("PBR_Albedo");
                Texture normal = LoadTexture("PBR_Free_NM");
                Texture mask = LoadTexture("PBR_Free_MS");
                Texture occlusion = LoadTexture("PBR_Free_AO");
                Texture emission = LoadTexture("PBR_Free_EM");

                if (albedo != null)
                    mat.SetTexture("_BaseMap", albedo);

                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (mask != null)
                {
                    mat.SetTexture("_MetallicGlossMap", mask);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Smoothness", 0.8f);
                }

                if (occlusion != null)
                {
                    mat.SetTexture("_OcclusionMap", occlusion);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                }

                if (emission != null)
                {
                    mat.SetTexture("_EmissionMap", emission);
                    mat.SetColor("_EmissionColor", Color.white);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            });
        }

        static Texture LoadTexture(string fileName)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>($"{k_Textures}/{fileName}.png");
            if (texture == null)
                Debug.LogWarning($"[TP04] Texture introuvable : {k_Textures}/{fileName}.png");

            return texture;
        }

        static Material CreateMaterial(string name, System.Action<Material> configure)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[TP04] Shader URP/Lit introuvable, le projet n'est peut-être pas en URP.");
                shader = Shader.Find("Standard");
            }

            string path = $"{k_Materials}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            configure(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            return material;
        }

        static float MeasureHeight(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y;
        }

        static GameObject SaveAndDiscard(GameObject root, string path)
        {
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return asset;
        }

        /// <summary>Removes any object with this name before creating a fresh one.</summary>
        static GameObject Replace(string name, params System.Type[] components)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing);

            return new GameObject(name, components);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void SetRef(Component target, string field, Object value)
        {
            if (target == null)
                return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning($"[TP04] Champ '{field}' introuvable sur {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Component target, string field, float value)
        {
            if (target == null)
                return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning($"[TP04] Champ '{field}' introuvable sur {target.GetType().Name}.");
                return;
            }

            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
