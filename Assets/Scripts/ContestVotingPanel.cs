using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Voting panel — fetches the contestant list from the server, displays vote buttons,
/// and sends votes via GET /world/{showId}/vote?round=N&contestant=ID&voter=Name.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContestVotingPanel : UdonSharpBehaviour
{
    [Header("References")]
    public ContestServerClient serverClient;
    public Text statusText;
    public Text[] contestantNameLabels;
    public Button[] voteButtons;
    public GameObject panelRoot;

    private int[] _contestantIds = new int[20];
    private string[] _contestantNames = new string[20];
    private int _contestantCount;
    private int _currentRound;
    private bool _hasVoted;
    private bool _requestInFlight;

    private const int STATE_IDLE = 0;
    private const int STATE_FETCHING_CONTESTANTS = 1;
    private const int STATE_VOTING = 2;
    private int _state = STATE_IDLE;

    public void SetRound(int roundNumber)
    {
        _currentRound = roundNumber;
        _hasVoted = false;
        RefreshContestants();
    }

    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void RefreshContestants()
    {
        if (_requestInFlight) return;
        _state = STATE_FETCHING_CONTESTANTS;
        _requestInFlight = true;
        string url = serverClient.BuildWorldUrl("contestants");
        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public void VoteForContestant0() { _CastVote(0); }
    public void VoteForContestant1() { _CastVote(1); }
    public void VoteForContestant2() { _CastVote(2); }
    public void VoteForContestant3() { _CastVote(3); }
    public void VoteForContestant4() { _CastVote(4); }
    public void VoteForContestant5() { _CastVote(5); }
    public void VoteForContestant6() { _CastVote(6); }
    public void VoteForContestant7() { _CastVote(7); }
    public void VoteForContestant8() { _CastVote(8); }
    public void VoteForContestant9() { _CastVote(9); }

    private void _CastVote(int index)
    {
        if (_hasVoted || _requestInFlight || index >= _contestantCount) return;

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) return;

        _state = STATE_VOTING;
        _requestInFlight = true;

        string voterName = _UrlEncode(localPlayer.displayName);
        string url = serverClient.BuildWorldUrlWithParams("vote",
            "round=" + _currentRound + "&contestant=" + _contestantIds[index] + "&voter=" + voterName);

        if (statusText != null) statusText.text = "Voting...";
        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requestInFlight = false;
        string response = result.Result;

        if (_state == STATE_FETCHING_CONTESTANTS)
        {
            _ParseContestants(response);
        }
        else if (_state == STATE_VOTING)
        {
            _HandleVoteResponse(response);
        }

        _state = STATE_IDLE;
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _state = STATE_IDLE;
        if (statusText != null) statusText.text = "Network error.";
    }

    private void _ParseContestants(string response)
    {
        string[] lines = response.Split('\n');
        string[] header = lines[0].Split('|');
        if (header[0] != "OK") return;

        _contestantCount = 0;
        for (int i = 1; i < lines.Length && _contestantCount < 20; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split('|');
            if (parts.Length < 2) continue;

            int id;
            if (int.TryParse(parts[0], out id))
            {
                _contestantIds[_contestantCount] = id;
                _contestantNames[_contestantCount] = parts[1];
                _contestantCount++;
            }
        }

        _UpdateUI();
    }

    private void _UpdateUI()
    {
        for (int i = 0; i < contestantNameLabels.Length; i++)
        {
            if (i < _contestantCount)
            {
                if (contestantNameLabels[i] != null)
                {
                    contestantNameLabels[i].text = _contestantNames[i];
                    contestantNameLabels[i].gameObject.SetActive(true);
                }
                if (i < voteButtons.Length && voteButtons[i] != null)
                {
                    voteButtons[i].gameObject.SetActive(true);
                    voteButtons[i].interactable = !_hasVoted;
                }
            }
            else
            {
                if (contestantNameLabels[i] != null)
                    contestantNameLabels[i].gameObject.SetActive(false);
                if (i < voteButtons.Length && voteButtons[i] != null)
                    voteButtons[i].gameObject.SetActive(false);
            }
        }
    }

    private void _HandleVoteResponse(string response)
    {
        string[] parts = response.Split('|');
        if (parts[0] == "OK")
        {
            _hasVoted = true;
            if (statusText != null) statusText.text = "Vote cast!";
            _DisableAllVoteButtons();
        }
        else if (parts.Length > 1 && parts[1] == "already_voted")
        {
            _hasVoted = true;
            if (statusText != null) statusText.text = "Already voted this round.";
            _DisableAllVoteButtons();
        }
        else
        {
            if (statusText != null) statusText.text = "Vote failed: " + (parts.Length > 1 ? parts[1] : "unknown");
        }
    }

    private void _DisableAllVoteButtons()
    {
        for (int i = 0; i < voteButtons.Length; i++)
        {
            if (voteButtons[i] != null) voteButtons[i].interactable = false;
        }
    }

    private string _UrlEncode(string input)
    {
        string encoded = "";
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~')
            {
                encoded += c;
            }
            else if (c == ' ')
            {
                encoded += "%20";
            }
            else
            {
                int val = (int)c;
                encoded += "%" + val.ToString("X2");
            }
        }
        return encoded;
    }
}
