using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class AudienceVotingManager : UdonSharpBehaviour
{
    [Header("Contestants")]
    [Tooltip("Display names for each contestant, matched by index to Vote Buttons")]
    [SerializeField] private string[] contestantNames;

    [Tooltip("One AudienceVoteButton per contestant, same order as names")]
    [SerializeField] private AudienceVoteButton[] voteButtons;

    [Header("UI")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text resultsText;
    [SerializeField] private GameObject votingPanel;
    [SerializeField] private GameObject resultsPanel;

    [Header("Server-Backed Mode (optional)")]
    [Tooltip("Assign to enable server-backed voting. Leave null for local UdonSynced mode.")]
    [SerializeField] private ContestServerClient serverClient;
    [Tooltip("Server-backed voting panel — used when serverClient is assigned")]
    [SerializeField] private ContestVotingPanel serverVotingPanel;
    [Tooltip("Server-backed leaderboard — used when serverClient is assigned")]
    [SerializeField] private ContestLeaderboardPanel serverLeaderboardPanel;

    [UdonSynced] private bool _votingOpen;
    [UdonSynced] private int _roundNumber;

    private bool _hasVoted;
    private int _lastSeenRound;

    public bool UseServerMode()
    {
        return serverClient != null;
    }

    public bool IsVotingOpen()
    {
        return _votingOpen;
    }

    public bool CanVote()
    {
        return _votingOpen && !_hasVoted;
    }

    public void OnVoteCast()
    {
        _hasVoted = true;
        RefreshUI();
    }

    // ── Host Controls (wire these to UI buttons) ──

    public void OpenVoting()
    {
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        _roundNumber++;
        _votingOpen = true;
        _hasVoted = false;
        _lastSeenRound = _roundNumber;
        RequestSerialization();

        for (int i = 0; i < voteButtons.Length; i++)
        {
            voteButtons[i].ResetVotes();
        }

        if (resultsPanel != null) resultsPanel.SetActive(false);

        if (UseServerMode() && serverVotingPanel != null)
        {
            serverVotingPanel.Show();
            serverVotingPanel.SetRound(_roundNumber);
        }

        RefreshUI();
    }

    public void CloseVoting()
    {
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        _votingOpen = false;
        RequestSerialization();

        if (UseServerMode())
        {
            if (serverVotingPanel != null) serverVotingPanel.Hide();
            if (serverLeaderboardPanel != null)
            {
                serverLeaderboardPanel.Show();
                serverLeaderboardPanel.StartPolling();
            }
        }

        ShowResults();
    }

    public void ResetAll()
    {
        if (!Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        _votingOpen = false;
        _hasVoted = false;
        RequestSerialization();

        for (int i = 0; i < voteButtons.Length; i++)
        {
            voteButtons[i].ResetVotes();
        }

        if (resultsPanel != null) resultsPanel.SetActive(false);
        RefreshUI();
    }

    // ── Sync ──

    public override void OnDeserialization()
    {
        if (_roundNumber != _lastSeenRound)
        {
            _lastSeenRound = _roundNumber;
            _hasVoted = false;
        }

        RefreshUI();

        if (!_votingOpen)
        {
            ShowResults();
        }
    }

    // ── Display ──

    private void RefreshUI()
    {
        if (votingPanel != null)
            votingPanel.SetActive(_votingOpen && !_hasVoted);

        if (statusText == null) return;

        if (!_votingOpen)
            statusText.text = "";
        else if (_hasVoted)
            statusText.text = "Vote recorded — waiting for results\u2026";
        else
            statusText.text = "Vote for your favorite!";
    }

    private void ShowResults()
    {
        if (voteButtons == null || voteButtons.Length == 0) return;

        int highestCount = 0;
        int totalVotes = 0;

        for (int i = 0; i < voteButtons.Length; i++)
        {
            int c = voteButtons[i].GetVoteCount();
            totalVotes += c;
            if (c > highestCount) highestCount = c;
        }

        // Build results string
        string text = "";
        string winnerName = "";
        int winnerCount = 0;
        bool tie = false;

        for (int i = 0; i < voteButtons.Length; i++)
        {
            int c = voteButtons[i].GetVoteCount();
            string label = i < contestantNames.Length ? contestantNames[i] : "Contestant " + (i + 1);
            text += label + "  —  " + c + (c == 1 ? " vote" : " votes") + "\n";

            if (c == highestCount && highestCount > 0)
            {
                if (winnerCount > 0) tie = true;
                winnerName = label;
                winnerCount++;
            }
        }

        text += "\n";

        if (totalVotes == 0)
            text += "No votes were cast.";
        else if (tie)
            text += "It's a tie!";
        else
            text += winnerName + " wins!";

        if (resultsText != null) resultsText.text = text;
        if (resultsPanel != null) resultsPanel.SetActive(true);
        if (votingPanel != null) votingPanel.SetActive(false);
    }
}
