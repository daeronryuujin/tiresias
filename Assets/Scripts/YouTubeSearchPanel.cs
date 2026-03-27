using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// In-world YouTube search panel. Player types a query, results come back from
/// the server's yt-dlp proxy, and selecting a result loads it into the video player.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class YouTubeSearchPanel : UdonSharpBehaviour
{
    [Header("Server")]
    public ContestServerClient serverClient;

    [Header("UI — Input")]
    public InputField searchInput;

    [Header("UI — Results")]
    public Text[] resultTitleLabels;
    public Text[] resultDetailLabels;
    public Button[] resultPlayButtons;
    public Text statusText;

    [Header("Video Player")]
    public VRC.SDK3.Video.Components.AVPro.VRCAVProVideoPlayer videoPlayer;

    private string[] _resultUrls = new string[10];
    private int _resultCount;
    private bool _requestInFlight;

    public void OnSearchClicked()
    {
        if (_requestInFlight) return;
        if (searchInput == null) return;

        string query = searchInput.text;
        if (string.IsNullOrEmpty(query)) return;

        string encoded = _UrlEncode(query.Trim());
        string url = serverClient.BuildYouTubeSearchUrl(encoded);

        _requestInFlight = true;
        if (statusText != null) statusText.text = "Searching...";

        VRCStringDownloader.LoadUrl(new VRCUrl(url), (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requestInFlight = false;
        _ParseResults(result.Result);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requestInFlight = false;
        if (statusText != null) statusText.text = "Search failed. Try again.";
    }

    private void _ParseResults(string response)
    {
        string[] lines = response.Split('\n');
        string[] header = lines[0].Split('|');
        if (header[0] != "OK")
        {
            if (statusText != null) statusText.text = "Error: " + (header.Length > 1 ? header[1] : "unknown");
            return;
        }

        _resultCount = 0;

        for (int i = 1; i < lines.Length && _resultCount < 10; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split('|');
            if (parts.Length < 5) continue;

            string title = parts[1];
            string channel = parts[2];
            int duration = 0;
            int.TryParse(parts[3], out duration);
            string videoUrl = parts[4];

            int minutes = duration / 60;
            int seconds = duration % 60;
            string durationStr = minutes + ":" + seconds.ToString("D2");

            if (_resultCount < resultTitleLabels.Length && resultTitleLabels[_resultCount] != null)
            {
                resultTitleLabels[_resultCount].text = title;
                resultTitleLabels[_resultCount].gameObject.SetActive(true);
            }
            if (_resultCount < resultDetailLabels.Length && resultDetailLabels[_resultCount] != null)
            {
                resultDetailLabels[_resultCount].text = channel + " | " + durationStr;
                resultDetailLabels[_resultCount].gameObject.SetActive(true);
            }
            if (_resultCount < resultPlayButtons.Length && resultPlayButtons[_resultCount] != null)
            {
                resultPlayButtons[_resultCount].gameObject.SetActive(true);
            }

            _resultUrls[_resultCount] = videoUrl;
            _resultCount++;
        }

        // Hide unused slots
        for (int i = _resultCount; i < 10; i++)
        {
            if (i < resultTitleLabels.Length && resultTitleLabels[i] != null)
                resultTitleLabels[i].gameObject.SetActive(false);
            if (i < resultDetailLabels.Length && resultDetailLabels[i] != null)
                resultDetailLabels[i].gameObject.SetActive(false);
            if (i < resultPlayButtons.Length && resultPlayButtons[i] != null)
                resultPlayButtons[i].gameObject.SetActive(false);
        }

        if (statusText != null) statusText.text = _resultCount + " results";
    }

    // Per-slot play methods (UdonSharp workaround for no delegates)
    public void PlayVideo0() { _PlayVideo(0); }
    public void PlayVideo1() { _PlayVideo(1); }
    public void PlayVideo2() { _PlayVideo(2); }
    public void PlayVideo3() { _PlayVideo(3); }
    public void PlayVideo4() { _PlayVideo(4); }
    public void PlayVideo5() { _PlayVideo(5); }
    public void PlayVideo6() { _PlayVideo(6); }
    public void PlayVideo7() { _PlayVideo(7); }
    public void PlayVideo8() { _PlayVideo(8); }
    public void PlayVideo9() { _PlayVideo(9); }

    private void _PlayVideo(int index)
    {
        if (index < 0 || index >= _resultCount) return;
        if (videoPlayer == null) return;

        VRCUrl vrcUrl = new VRCUrl(_resultUrls[index]);
        videoPlayer.PlayURL(vrcUrl);

        if (statusText != null)
        {
            string title = (index < resultTitleLabels.Length && resultTitleLabels[index] != null)
                ? resultTitleLabels[index].text
                : "video";
            statusText.text = "Playing: " + title;
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
                encoded += "+";
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
