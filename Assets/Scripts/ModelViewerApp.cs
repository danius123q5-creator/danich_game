using UnityEngine;

/// <summary>3.7: standalone host for the MODEL VIEWER. When the player is built with the product
/// name "ZombieShooterModelViewer" (see BuildScript.BuildModelViewer), GameBootstrap.Boot spawns
/// this instead of the game — it creates a camera, runs the ModelViewer turntable, and quits when
/// you press Back/Esc. Lets the model viewer ship as its OWN .exe alongside the game.</summary>
public class ModelViewerApp : MonoBehaviour
{
    ModelViewer viewer;

    void Start()
    {
        var camGO = new GameObject("ViewerCamera");
        camGO.transform.SetParent(transform, false);
        camGO.transform.position = new Vector3(0f, 3f, -8f);
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.14f, 0.16f, 0.20f);
        camGO.AddComponent<AudioListener>();

        var vgo = new GameObject("ModelViewer");
        vgo.transform.SetParent(transform, false);
        viewer = vgo.AddComponent<ModelViewer>();
        viewer.Init(cam);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnGUI()
    {
        UI.Begin(); // scale the overlay to the window (same virtual canvas as the game)
        if (viewer != null && viewer.DrawGUI()) Application.Quit();
    }
}
