// TEMPORARY test of the bundled-AI path. Deleted after use.
using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class TmpLocalAITest {
  const string Key = "TOT_LOCALAI_TEST";
  public static void Run() { EditorSceneManager.OpenScene("Assets/Scenes/Play_Scene.unity"); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }
  [InitializeOnLoadMethod] static void Hook() { EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) new GameObject("TmpRunner").AddComponent<Runner>(); }; }
  public class Runner : MonoBehaviour {
    static void Shot(string name) { var cam = Camera.main; var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.DefaultHDR); cam.targetTexture = rt; cam.Render(); var ldr = new RenderTexture(960, 540, 0); Graphics.Blit(rt, ldr); RenderTexture.active = ldr; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0,0,960,540),0,0); tex.Apply(); System.IO.File.WriteAllBytes("/private/tmp/claude-501/-Users-andres-Documents-GitHub-GameJam-AndMarSiyXia/66ae0985-cade-4b2f-8d73-3e6fbdaec179/scratchpad/" + name + ".png", tex.EncodeToPNG()); cam.targetTexture = null; RenderTexture.active = null; }
    IEnumerator Start() {
      yield return new WaitForSeconds(1f);
      var ai = FindAnyObjectByType<NPCDialogueAI>();
      typeof(NPCDialogueAI).GetField("backend", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ai, NPCDialogueAI.Backend.LocalServer);
      Debug.Log("TEST available=" + LocalLLMServer.IsAvailable + " root=" + LocalLLMServer.RootFolder);
      float t0 = Time.realtimeSinceStartup; string reply = null;
      var msgs = new List<NPCDialogueAI.ChatMessage> {
        new NPCDialogueAI.ChatMessage("system", "You are Evelyn, an ordinary neighbour answering your front door on Halloween night. Facts: You have a cat called Pumpkin. Reply in 1-2 short spoken sentences."),
        new NPCDialogueAI.ChatMessage("assistant", "Oh, hello there! Happy Halloween."),
        new NPCDialogueAI.ChatMessage("user", "What's your cat's name?") };
      yield return ai.GenerateChat(msgs, r => reply = r);
      Debug.Log("TEST state=" + LocalLLMServer.State + " reply=\"" + reply + "\" seconds=" + (Time.realtimeSinceStartup - t0).ToString("F1"));
      float t1 = Time.realtimeSinceStartup; string reply2 = null;
      msgs.Add(new NPCDialogueAI.ChatMessage("assistant", reply)); msgs.Add(new NPCDialogueAI.ChatMessage("user", "Did you dress up last year?"));
      yield return ai.GenerateChat(msgs, r => reply2 = r);
      Debug.Log("TEST second reply=\"" + reply2 + "\" seconds=" + (Time.realtimeSinceStartup - t1).ToString("F1"));
      // Wrap-around: walk off the east edge facing outward.
      var player = FindAnyObjectByType<FirstPersonController_TEST>(); var field = FindAnyObjectByType<FieldBounds>(); var cc = player.GetComponent<CharacterController>();
      cc.enabled = false; player.transform.position = field.Center + new Vector3(field.radiusX * 0.98f, player.transform.position.y, 0f); player.transform.rotation = Quaternion.LookRotation(Vector3.right); cc.enabled = true;
      yield return null; Shot("wrap_before");
      for (int i = 0; i < 30; i++) { cc.Move(Vector3.right * 0.2f); yield return null; }
      Vector3 toCenter = field.Center - player.transform.position; toCenter.y = 0;
      float facing = Vector3.Dot(player.transform.forward, toCenter.normalized);
      Debug.Log("TEST wrap: pos=" + player.transform.position + " facing-center dot=" + facing.ToString("F2") + (facing > 0.95f ? " PASS" : " FAIL"));
      yield return null; Shot("wrap_after");
      SessionState.SetBool(Key, false); EditorApplication.ExitPlaymode(); yield return null;
      EditorApplication.Exit(reply != null && reply.Contains("Pumpkin") ? 0 : 1);
    }
  }
}
