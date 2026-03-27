using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Signup panel — player clicks the signup button to register as a contestant
/// for the current show. Sends GET /world/{showId}/signup?name={playerName}.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContestSignupPanel : UdonSharpBehaviour
{
    [Header("References")]
    public ContestServerClient serverClient;
    public Text statusText;
    public Button signupButton;

    private bool _requestInFlight;

    public void OnSignupClicked()
    {
        if (_requestInFlight) return;

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) return;

        string playerName = localPlayer.displayName;
        string url = serverClient.BuildWorldUrlWithParams("signup", "name=" + _UrlEncode(playerName));

        if (statusText != null) statusText.text = "Signing up...";
        _requestInFlight = true;

        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requestInFlight = false;
        string response = result.Result;
        string[] parts = response.Split('|');

        if (parts[0] == "OK")
        {
            if (statusText != null) statusText.text = "Signed up!";
            if (signupButton != null) signupButton.interactable = false;
        }
        else if (parts.Length > 1 && parts[1] == "already_signed_up")
        {
            if (statusText != null) statusText.text = "Already signed up.";
            if (signupButton != null) signupButton.interactable = false;
        }
        else
        {
            if (statusText != null) statusText.text = "Signup failed: " + (parts.Length > 1 ? parts[1] : "unknown error");
        }
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requestInFlight = false;
        if (statusText != null) statusText.text = "Network error. Try again.";
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
