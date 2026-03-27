using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class AudienceVoteButton : UdonSharpBehaviour
{
    [SerializeField] private AudienceVotingManager manager;
    [Tooltip("Optional label that shows the live vote count")]
    [SerializeField] private Text countLabel;

    [UdonSynced] private int _voteCount;

    public int GetVoteCount()
    {
        return _voteCount;
    }

    public override void Interact()
    {
        if (manager == null) return;
        if (!manager.CanVote()) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        _voteCount++;
        RequestSerialization();
        UpdateLabel();
        manager.OnVoteCast();
    }

    public void ResetVotes()
    {
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        _voteCount = 0;
        RequestSerialization();
        UpdateLabel();
    }

    public override void OnDeserialization()
    {
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (countLabel != null)
            countLabel.text = _voteCount.ToString();
    }
}
