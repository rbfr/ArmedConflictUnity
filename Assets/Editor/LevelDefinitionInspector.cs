using UnityEditor;
using UnityEngine;
using ArmedConflict.Data;

/// <summary>
/// Renders LevelComposition's six-rule check live under a level's inspector.
///
/// Thin on purpose: the rules and their thresholds live in LevelComposition, which is also the
/// headless entry point (`-executeMethod LevelComposition.Report`). Two copies of a threshold is
/// how the rules drifted from the levels in the first place.
/// </summary>
[CustomEditor(typeof(LevelDefinitionSO))]
public class LevelDefinitionInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var level = (LevelDefinitionSO)target;
        EditorGUILayout.Space();

        if (level.isTestLevel)
        {
            EditorGUILayout.HelpBox(
                "Test rig — composition rules not enforced. Rigs exist to break a rule on " +
                "purpose and measure what happens.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField("Composition — LEVEL_AUTHORING.md", EditorStyles.boldLabel);

        var findings = LevelComposition.Check(level, out string buildError);
        if (buildError != null)
        {
            EditorGUILayout.HelpBox($"Cannot measure yet — the level does not build.\n{buildError}",
                                    MessageType.None);
            return;
        }

        foreach (var f in findings)
            EditorGUILayout.HelpBox(f.Text, f.Level switch
            {
                LevelComposition.Severity.Error => MessageType.Error,
                LevelComposition.Severity.Warn => MessageType.Warning,
                _ => MessageType.Info,
            });
    }
}
