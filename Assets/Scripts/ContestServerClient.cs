using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Base helper for making HTTP GET requests to the contest server.
/// Other contest scripts reference this to get the base URL and show ID.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContestServerClient : UdonSharpBehaviour
{
    [Header("Server Configuration")]
    public string baseUrl = "https://example.com";
    public string showId = "1";

    public string BuildWorldUrl(string path)
    {
        return baseUrl + "/world/" + showId + "/" + path;
    }

    public string BuildWorldUrlWithParams(string path, string queryString)
    {
        return baseUrl + "/world/" + showId + "/" + path + "?" + queryString;
    }

    public string BuildYouTubeSearchUrl(string query)
    {
        return baseUrl + "/world/youtube/search?q=" + query;
    }

    public void SendRequest(VRCUrl url, IUdonEventReceiver callback)
    {
        VRCStringDownloader.LoadUrl(url, callback);
    }
}
