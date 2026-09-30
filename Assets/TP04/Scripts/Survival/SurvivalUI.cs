using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TP04.Survival
{
    /// <summary>
    /// Binds the world space canvas to the game manager: menu panel, in-game HUD and game over
    /// panel. All references are wired by the editor setup, nothing has to be dragged by hand.
    /// </summary>
    public class SurvivalUI : MonoBehaviour
    {
        [Header("Panneaux")]
        [SerializeField] GameObject m_MenuPanel = null;
        [SerializeField] GameObject m_HudPanel = null;
        [SerializeField] GameObject m_GameOverPanel = null;

        [Header("Menu")]
        [SerializeField] Button m_StartButton = null;
        [SerializeField] Button m_Arc90Button = null;
        [SerializeField] Button m_Arc180Button = null;
        [SerializeField] Button m_Arc360Button = null;
        [SerializeField] TextMeshProUGUI m_BestTimeLabel = null;

        [Header("HUD")]
        [SerializeField] TextMeshProUGUI m_TimeLabel = null;
        [SerializeField] TextMeshProUGUI m_LivesLabel = null;
        [SerializeField] TextMeshProUGUI m_KillsLabel = null;

        [Header("Game over")]
        [SerializeField] TextMeshProUGUI m_ResultLabel = null;
        [SerializeField] Button m_RestartButton = null;
        [SerializeField] Button m_BackToMenuButton = null;

        [Header("Couleurs des boutons d'angle")]
        [SerializeField] Color m_SelectedColor = new Color(0.16f, 0.62f, 0.94f);
        [SerializeField] Color m_UnselectedColor = new Color(0.18f, 0.20f, 0.24f);

        SurvivalGameManager m_Manager;

        void Start()
        {
            m_Manager = SurvivalGameManager.instance;

            if (m_Manager == null)
            {
                Debug.LogError("[SurvivalUI] Aucun SurvivalGameManager dans la scène.", this);
                enabled = false;
                return;
            }

            Bind(m_StartButton, () => m_Manager.StartGame());
            Bind(m_RestartButton, () => m_Manager.StartGame());
            Bind(m_BackToMenuButton, () => m_Manager.StopGame());
            Bind(m_Arc90Button, () => m_Manager.SetSpawnArcDegrees(90));
            Bind(m_Arc180Button, () => m_Manager.SetSpawnArcDegrees(180));
            Bind(m_Arc360Button, () => m_Manager.SetSpawnArcDegrees(360));

            m_Manager.stateChanged += OnStateChanged;
            m_Manager.timeChanged += OnTimeChanged;
            m_Manager.hitsChanged += OnHitsChanged;
            m_Manager.killsChanged += OnKillsChanged;
            m_Manager.spawnArcChanged += OnSpawnArcChanged;

            OnStateChanged(m_Manager.state);
            OnSpawnArcChanged(m_Manager.spawnArc);
            OnTimeChanged(m_Manager.survivedTime);
            OnKillsChanged(m_Manager.kills);

            if (m_Manager.playerHealth != null)
                OnHitsChanged(m_Manager.playerHealth.hitsTaken, m_Manager.playerHealth.hitsAllowed);
        }

        void OnDestroy()
        {
            if (m_Manager == null)
                return;

            m_Manager.stateChanged -= OnStateChanged;
            m_Manager.timeChanged -= OnTimeChanged;
            m_Manager.hitsChanged -= OnHitsChanged;
            m_Manager.killsChanged -= OnKillsChanged;
            m_Manager.spawnArcChanged -= OnSpawnArcChanged;
        }

        static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void OnStateChanged(GameState state)
        {
            SetActive(m_MenuPanel, state == GameState.Menu);
            SetActive(m_HudPanel, state == GameState.Playing);
            SetActive(m_GameOverPanel, state == GameState.GameOver);

            if (state == GameState.Menu && m_BestTimeLabel != null)
            {
                m_BestTimeLabel.text = m_Manager.bestTime > 0f
                    ? $"Meilleur temps : {SurvivalGameManager.FormatTime(m_Manager.bestTime)}"
                    : "Aucun record pour l'instant";
            }

            if (state == GameState.GameOver && m_ResultLabel != null)
            {
                m_ResultLabel.text =
                    $"Tu as tenu {SurvivalGameManager.FormatTime(m_Manager.survivedTime)}\n" +
                    $"{m_Manager.kills} robot{(m_Manager.kills > 1 ? "s" : string.Empty)} détruit{(m_Manager.kills > 1 ? "s" : string.Empty)}\n" +
                    $"Record : {SurvivalGameManager.FormatTime(m_Manager.bestTime)}";
            }
        }

        void OnTimeChanged(float seconds)
        {
            if (m_TimeLabel != null)
                m_TimeLabel.text = SurvivalGameManager.FormatTime(seconds);
        }

        void OnHitsChanged(int taken, int allowed)
        {
            if (m_LivesLabel == null)
                return;

            // Plain digits on purpose: the default TMP font atlas (LiberationSans SDF) has no
            // heart or bullet glyph, they would render as empty boxes in the headset.
            int remaining = Mathf.Max(0, allowed - taken);
            m_LivesLabel.text = $"{remaining} / {allowed}";
        }

        void OnKillsChanged(int kills)
        {
            if (m_KillsLabel != null)
                m_KillsLabel.text = $"x{kills}";
        }

        void OnSpawnArcChanged(SpawnArc arc)
        {
            Highlight(m_Arc90Button, arc == SpawnArc.Front90);
            Highlight(m_Arc180Button, arc == SpawnArc.Front180);
            Highlight(m_Arc360Button, arc == SpawnArc.All360);
        }

        void Highlight(Button button, bool selected)
        {
            if (button == null)
                return;

            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = selected ? m_SelectedColor : m_UnselectedColor;
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
