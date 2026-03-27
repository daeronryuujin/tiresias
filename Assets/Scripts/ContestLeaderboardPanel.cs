using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Leaderboard panel — polls /world/{showId}/leaderboard on a timer
/// and renders cumulative standings on a Text component.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContestLeaderboardPanel : UdonSharpBehaviour
{
    [Header("References")]
    public ContestServerClient serverClient;
    public Text leaderboardText;
    public GameObject panelRoot;

    [Header("Settings")]
    public float pollIntervalSeconds = 15f;

    private bool _requestInFlight;
    private bool _polling;

    public void StartPolling()
    {
        _polling = true;
        _FetchLeaderboard();
    }

    public void StopPolling()
    {
        _polling = false;
    }

    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void _FetchLeaderboard()
    {
        if (_requestInFlight) return;
        _requestInFlight = true;
        string url = serverClient.BuildWorldUrl("leaderboard");
        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public void _ScheduleNextPoll()
    {
        if (_polling)
        {
            SendCustomEventDelayedSeconds(nameof(_FetchLeaderboard), pollIntervalSeconds);
        }
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _ParseLeaderboard(result.Result);
        _ScheduleNextPoll();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _ScheduleNextPoll();
    }

    private void _ParseLeaderboard(string response)
    {
        string[] lines = response.Split('\n');
        string[] header = lines[0].Split('|');
        if (header[0] != "OK") return;

        string display = "=== LEADERBOARD ===\n\n";
        int rank = 1;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split('|');
            if (parts.Length < 4) continue;

            string name = parts[1];
            string votes = parts[2];
            string rounds = parts[3];

            display += "#" + rank + "  " + name + "  —  " + votes + " votes (" + rounds + " rounds)\n";
            rank++;
        }

        if (rank == 1)
        {
            display += "No standings yet.";
        }

        if (leaderboardText != null)
        {
            leaderboardText.text = display;
        }
    }
}
