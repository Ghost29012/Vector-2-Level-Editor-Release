using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Vectorier/DynamicColor")]
public class DynamicColor : MonoBehaviour
{
    [SerializeField]
    public string TransformationName = "";
    [Tooltip("Duration in seconds")] public float Duration = 1.0f;
    [Tooltip("Starting Color")] public Color StartColor = Color.white;
    [Tooltip("Color to change into")] public Color EndColor = Color.white;

    private SpriteRenderer spriteRenderer;
    private Dictionary<SpriteRenderer, Color> savedColors = new Dictionary<SpriteRenderer, Color>();
    private bool isTransitioning = false;
    public bool IsTransitioning => isTransitioning;

    private void GetSpriteRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

    public void PreviewColor(Action onFinish = null)
    {
#if UNITY_EDITOR
        if (isTransitioning) return;

        if (gameObject.CompareTag("Dynamic"))
        {
            SpriteRenderer[] childRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (var childRenderer in childRenderers)
            {
                if (childRenderer.gameObject != this.gameObject)
                {
                    if (!savedColors.ContainsKey(childRenderer))
                    {
                        savedColors[childRenderer] = childRenderer.color;
                    }
                    EditorCoroutine.Start(GraduallyChangeColor(childRenderer, StartColor, EndColor, Duration, null));
                }
            }
        }
        else
        {
            GetSpriteRenderer();
            if (spriteRenderer != null)
            {
                if (!savedColors.ContainsKey(spriteRenderer))
                {
                    savedColors[spriteRenderer] = spriteRenderer.color;
                }
                EditorCoroutine.Start(GraduallyChangeColor(spriteRenderer, StartColor, EndColor, Duration, null));
            }
        }
#endif
    }

    public void ResetColor()
    {
        if (gameObject.CompareTag("Dynamic"))
        {
            SpriteRenderer[] childRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (var childRenderer in childRenderers)
            {
                if (childRenderer.gameObject != this.gameObject && savedColors.ContainsKey(childRenderer))
                {
                    childRenderer.color = savedColors[childRenderer];
                }
            }
        }
        else
        {
            GetSpriteRenderer();
            if (spriteRenderer != null && savedColors.ContainsKey(spriteRenderer))
            {
                spriteRenderer.color = savedColors[spriteRenderer];
            }
        }
        isTransitioning = false;
    }

    private IEnumerator GraduallyChangeColor(SpriteRenderer targetRenderer, Color startColor, Color endColor, float duration, Action onFinish)
    {
        isTransitioning = true;
        float elapsedTime = 0f;

        targetRenderer.color = startColor;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            targetRenderer.color = Color.Lerp(startColor, endColor, elapsedTime / duration);
#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
            yield return null;
        }

        targetRenderer.color = endColor;
#if UNITY_EDITOR
        yield return new EditorWaitForSeconds(0.5f);
#else
        yield return new WaitForSeconds(0.5f);
#endif

        ResetColor();
        onFinish?.Invoke();
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(DynamicColor))]
public class DynamicColorButton : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DynamicColor dynamicColorComponent = (DynamicColor)target;

        GUI.enabled = !dynamicColorComponent.IsTransitioning;

        if (GUILayout.Button("Preview Color"))
        {
            dynamicColorComponent.PreviewColor(() =>
            {
                MarkObjectAsDirty(dynamicColorComponent);
            });
        }

        GUI.enabled = true;
    }

    private void MarkObjectAsDirty(DynamicColor dynamicColorComponent)
    {
        EditorUtility.SetDirty(dynamicColorComponent);
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
}

public class EditorCoroutine
{
    public static IEnumerator Start(IEnumerator update, Action onFinish = null)
    {
        EditorApplication.CallbackFunction callback = null;
        callback = () =>
        {
            try
            {
                if (update.MoveNext() == false)
                {
                    EditorApplication.update -= callback;
                    onFinish?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.update -= callback;
            }
        };

        EditorApplication.update += callback;
        return update;
    }
}

public class EditorWaitForSeconds : IEnumerator
{
    private float waitTime;
    private float startTime;

    public EditorWaitForSeconds(float time)
    {
        waitTime = time;
        startTime = (float)EditorApplication.timeSinceStartup;
    }

    public bool MoveNext()
    {
        return (float)EditorApplication.timeSinceStartup < startTime + waitTime;
    }

    public void Reset() { }
    public object Current { get { return null; } }
}
#endif
