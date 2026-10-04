using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;

public class TimelineController : MonoBehaviour
{
    public int maxHistory = 500;
    public bool IsRewinding { get; private set; } = false;

    private Queue<string>  _localHistory = new Queue<string>(); // O(1) enqueue/dequeue
    private string[]       _historyArray; // snapshot for indexed access during rewind
    private int            _currentIndex = 0;
    private NetworkManager _networkManager;
    private GUIStyle       _rewindStyle; // cached — never allocated inside OnGUI

    void Awake()
    {
        _networkManager = FindObjectOfType<NetworkManager>();

        // Cache the GUIStyle once — NEVER allocate inside OnGUI
        _rewindStyle = new GUIStyle
        {
            fontSize  = 24,
            fontStyle = FontStyle.Bold
        };
        _rewindStyle.normal.textColor = Color.red;
    }
    
    void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        
        // Press T to toggle Timeline mode
        if (keyboard.tKey.wasPressedThisFrame)
        {
            if (IsRewinding) ResumeLive();
            else ToggleRewind();
        }
        
        // Scrub through time
        if (IsRewinding)
        {
            if (keyboard.leftCtrlKey.wasPressedThisFrame) StepBackward();
            if (keyboard.leftAltKey.wasPressedThisFrame) StepForward();
        }
    }

    void OnGUI()
    {
        if (IsRewinding)
            // Use cached style — zero allocations per frame
            GUI.Label(new Rect(20, Screen.height - 40, 800, 40),
                $"[ TIMELINE REWIND ] {GetCurrentRoundLabel()}  (Use L-CTRL / L-ALT)",
                _rewindStyle);
    }

    public void RecordSnapshot(string json)
    {
        _localHistory.Enqueue(json);
        if (_localHistory.Count > maxHistory)
            _localHistory.Dequeue(); // O(1) — no array shifting
    }

    public void ToggleRewind()
    {
        IsRewinding = true;
        // Snapshot queue to array once for indexed access
        _historyArray = _localHistory.ToArray();
        _currentIndex = _historyArray.Length - 1;
        Debug.Log($"[Timeline] Rewind activated at index {_currentIndex}");
    }

    public void ResumeLive()
    {
        IsRewinding = false;
        _historyArray = null;
        Debug.Log("[Timeline] Resumed live mode");
    }

    public void StepBackward()
    {
        if (!IsRewinding || _historyArray == null || _historyArray.Length == 0) return;
        _currentIndex = Mathf.Max(0, _currentIndex - 1);
        RenderHistoricalState();
    }

    public void StepForward()
    {
        if (!IsRewinding || _historyArray == null || _historyArray.Length == 0) return;
        _currentIndex = Mathf.Min(_historyArray.Length - 1, _currentIndex + 1);
        RenderHistoricalState();
    }

    public void SetIndex(int index)
    {
        if (!IsRewinding || _historyArray == null || _historyArray.Length == 0) return;
        _currentIndex = Mathf.Clamp(index, 0, _historyArray.Length - 1);
        RenderHistoricalState();
    }

    void RenderHistoricalState()
    {
        if (_historyArray == null || _currentIndex < 0 || _currentIndex >= _historyArray.Length) return;
        string json = _historyArray[_currentIndex];
        try
        {
            ServerState state = JsonConvert.DeserializeObject<ServerState>(json);
            if (_networkManager != null)
                _networkManager.RenderHistoricalState(state);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Timeline] Failed to parse historical state: {ex.Message}");
        }
    }

    public string GetCurrentRoundLabel()
    {
        int total = _historyArray?.Length ?? _localHistory.Count;
        if (!IsRewinding || total == 0)
            return $"Round {_localHistory.Count} (LIVE)";
        return $"Round {_currentIndex + 1} / {total} (HISTORICAL)";
    }

    public int HistoryCount => _localHistory.Count;
}
