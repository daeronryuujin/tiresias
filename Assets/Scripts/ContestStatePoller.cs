using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Polls /world/{showId}/state on a timer and drives UI visibility
/// for signup, voting, and leaderboard panels based on show/round state.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContestStatePoller : UdonSharpBehaviour
{
    [Header("References")]
    public ContestServerClient serverClient;
    public ContestSignupPanel signupPanel;
    public ContestVotingPanel votingPanel;
    public ContestLeaderboardPanel leaderboardPanel;

    [Header("Settings")]
    public float pollIntervalSeconds = 10f;

    [HideInInspector] public string showStatus = "";
    [HideInInspector] public int currentRound;
    [HideInInspector] public string roundStatus = "";

    private bool _requestInFlight;
    private bool _polling;
    private string _previousShowStatus = "";
    private string _previousRoundStatus = "";
    private int _previousRound;

    private void Start()
    {
        _polling = true;
        _FetchState();
    }

    public void _FetchState()
    {
        if (_requestInFlight) return;
        _requestInFlight = true;
        string url = serverClient.BuildWorldUrl("state");
        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public void _ScheduleNextPoll()
    {
        if (_polling)
        {
            SendCustomEventDelayedSeconds(nameof(_FetchState), pollIntervalSeconds);
        }
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _ParseState(result.Result);
        _ScheduleNextPoll();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _ScheduleNextPoll();
    }

    private void _ParseState(string response)
    {
        string[] parts = response.Split('|');
        if (parts[0] != "OK" || parts.Length < 4) return;

        showStatus = parts[1];

        int parsedRound;
        if (int.TryParse(parts[2], out parsedRound))
        {
            currentRound = parsedRound;
        }

        roundStatus = parts[3];

        bool stateChanged = showStatus != _previousShowStatus
            || roundStatus != _previousRoundStatus
            || currentRound != _previousRound;

        if (stateChanged)
        {
            _previousShowStatus = showStatus;
            _previousRoundStatus = roundStatus;
            _previousRound = currentRound;
            _UpdatePanels();
        }
    }

    private void _UpdatePanels()
    {
        if (showStatus == "upcoming" || showStatus == "active")
        {
            if (signupPanel != null)
            {
                signupPanel.gameObject.SetActive(true);
            }
        }
        else
        {
            if (signupPanel != null)
            {
                signupPanel.gameObject.SetActive(false);
            }
        }

        if (roundStatus == "voting")
        {
            if (votingPanel != null)
            {
                votingPanel.Show();
                votingPanel.SetRound(currentRound);
            }
            if (leaderboardPanel != null)
            {
                leaderboardPanel.Show();
                leaderboardPanel.StartPolling();
            }
        }
        else if (roundStatus == "closed")
        {
            if (votingPanel != null)
            {
                votingPanel.Hide();
            }
            if (leaderboardPanel != null)
            {
                leaderboardPanel.Show();
                leaderboardPanel.StartPolling();
            }
        }
        else
        {
            if (votingPanel != null)
            {
                votingPanel.Hide();
            }
            if (leaderboardPanel != null)
            {
                leaderboardPanel.StopPolling();
            }
        }
    }
}
