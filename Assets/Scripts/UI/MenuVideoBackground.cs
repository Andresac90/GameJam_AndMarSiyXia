using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays the main menu video from StreamingAssets by URL. WebGL can't play VideoClip assets,
/// so the old setup left an empty (black) render texture over the whole menu in web builds.
/// The RawImage stays hidden until playback starts, so the still background shows meanwhile.
/// In WebGL the video starts muted (browsers block autoplay with sound) and unmutes on the first click/key.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class MenuVideoBackground : MonoBehaviour
{
    public string fileName = "MainMenuHQ2.mp4";
    public RawImage target;

    VideoPlayer player;
    bool waitingToUnmute;

    void Awake()
    {
        player = GetComponent<VideoPlayer>();
        if (!target) target = GetComponent<RawImage>();
        if (target) target.enabled = false;

        player.playOnAwake = false;
        player.source = VideoSource.Url;
        player.url = Application.streamingAssetsPath + "/" + fileName;
        player.isLooping = true;
        player.started += _ => { if (target) target.enabled = true; };
        player.errorReceived += (_, message) => Debug.LogWarning("Menu video failed: " + message);

#if UNITY_WEBGL && !UNITY_EDITOR
        for (ushort i = 0; i < player.controlledAudioTrackCount; i++) player.SetDirectAudioMute(i, true);
        waitingToUnmute = true;
#endif
    }

    void Start() => player.Play();

    void Update()
    {
        if (!waitingToUnmute) return;
        bool input = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                     (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);
        if (!input) return;

        waitingToUnmute = false;
        for (ushort i = 0; i < player.controlledAudioTrackCount; i++) player.SetDirectAudioMute(i, false);
    }
}
