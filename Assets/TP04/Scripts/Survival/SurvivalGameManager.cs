using System;
using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// How wide the arc in front of the player is when robots spawn.
    /// The value is the arc width in degrees.
    /// </summary>
    public enum SpawnArc
    {
        Front90 = 90,
        Front180 = 180,
        All360 = 360,
    }

    public enum GameState
    {
        Menu,
        Playing,
        GameOver,
    }

    /// <summary>
    /// Owns the run: state machine, survival timer, score and the settings the menu drives.
    /// Everything else in the survival mode talks to this.
    /// </summary>
    public class SurvivalGameManager : MonoBehaviour
    {
        const string k_BestTimeKey = "TP04.Survival.BestTime";

        public static SurvivalGameManager instance { get; private set; }

        [SerializeField]
        [Tooltip("Spawner driven by this manager")]
        EnemySpawner m_Spawner = null;

        [SerializeField]
        [Tooltip("Player hit box, usually a child of the XR Origin camera")]
        PlayerHealth m_PlayerHealth = null;

        [SerializeField]
        [Tooltip("Arc selected by default in the menu")]
        SpawnArc m_SpawnArc = SpawnArc.Front180;

        [SerializeField]
        [Tooltip("Seconds of grace before the first robot appears")]
        float m_StartDelay = 2f;

        /// <summary>Raised whenever the run changes state (menu, playing, game over).</summary>
        public event Action<GameState> stateChanged;

        /// <summary>Raised every frame while playing, with the number of seconds survived.</summary>
        public event Action<float> timeChanged;

        /// <summary>Raised when the player is hit, with (hits taken, hits allowed).</summary>
        public event Action<int, int> hitsChanged;

        /// <summary>Raised when the selected spawn arc changes.</summary>
        public event Action<SpawnArc> spawnArcChanged;

        /// <summary>Raised when a robot is destroyed by the player, with the new kill count.</summary>
        public event Action<int> killsChanged;

        public GameState state { get; private set; } = GameState.Menu;
        public float survivedTime { get; private set; }
        public float bestTime { get; private set; }
        public int kills { get; private set; }
        public PlayerHealth playerHealth => m_PlayerHealth;

        public SpawnArc spawnArc
        {
            get => m_SpawnArc;
            set
            {
                if (m_SpawnArc == value)
                    return;

                m_SpawnArc = value;
                spawnArcChanged?.Invoke(m_SpawnArc);
            }
        }

        void Awake()
        {
            instance = this;
            bestTime = PlayerPrefs.GetFloat(k_BestTimeKey, 0f);
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void Start()
        {
            if (m_PlayerHealth != null)
                m_PlayerHealth.died += OnPlayerDied;

            SetState(GameState.Menu);
        }

        void Update()
        {
            if (state != GameState.Playing)
                return;

            survivedTime += Time.deltaTime;
            timeChanged?.Invoke(survivedTime);
        }

        public void StartGame()
        {
            if (state == GameState.Playing)
                return;

            ClearRobots();

            survivedTime = 0f;
            kills = 0;

            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.ResetHealth();
                hitsChanged?.Invoke(0, m_PlayerHealth.hitsAllowed);
            }

            killsChanged?.Invoke(0);
            timeChanged?.Invoke(0f);

            SetState(GameState.Playing);

            if (m_Spawner != null)
                m_Spawner.BeginSpawning(m_SpawnArc, m_StartDelay);
        }

        public void StopGame()
        {
            if (state == GameState.Menu)
                return;

            ClearRobots();
            SetState(GameState.Menu);
        }

        /// <summary>Called by the buttons of the menu. Takes the arc width in degrees.</summary>
        public void SetSpawnArcDegrees(int degrees)
        {
            switch (degrees)
            {
                case 90:
                    spawnArc = SpawnArc.Front90;
                    break;
                case 180:
                    spawnArc = SpawnArc.Front180;
                    break;
                default:
                    spawnArc = SpawnArc.All360;
                    break;
            }
        }

        /// <summary>Called by <see cref="PlayerHealth"/> so the UI can react to every hit.</summary>
        public void NotifyPlayerHit(int hitsTaken, int hitsAllowed)
        {
            hitsChanged?.Invoke(hitsTaken, hitsAllowed);
        }

        /// <summary>Called by <see cref="RobotEnemy"/> when the player destroys it.</summary>
        public void NotifyRobotKilled()
        {
            if (state != GameState.Playing)
                return;

            kills++;
            killsChanged?.Invoke(kills);
        }

        void OnPlayerDied()
        {
            if (state != GameState.Playing)
                return;

            if (m_Spawner != null)
                m_Spawner.StopSpawning();

            ClearRobots();

            if (survivedTime > bestTime)
            {
                bestTime = survivedTime;
                PlayerPrefs.SetFloat(k_BestTimeKey, bestTime);
                PlayerPrefs.Save();
            }

            SetState(GameState.GameOver);
        }

        void ClearRobots()
        {
            if (m_Spawner != null)
                m_Spawner.DespawnAll();
        }

        void SetState(GameState newState)
        {
            state = newState;
            stateChanged?.Invoke(state);
        }

        /// <summary>Formats a duration the way the HUD shows it, e.g. "01:07".</summary>
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
