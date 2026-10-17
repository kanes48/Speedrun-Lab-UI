using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class VerticalGradient : BaseMeshEffect
{
    [SerializeField] private Color topColor = Color.white;
    [SerializeField] private Color bottomColor = Color.gray;

    public void SetColors(Color top, Color bottom)
    {
        topColor = top;
        bottomColor = bottom;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive())
            return;

        var vertices = new System.Collections.Generic.List<UIVertex>();
        vertexHelper.GetUIVertexStream(vertices);

        for (int i = 0; i < vertices.Count; i++)
        {
            UIVertex vertex = vertices[i];

            // UI vertices are ordered around the rectangle.
            // Compare their vertical position to interpolate the color.
            float height = graphic.rectTransform.rect.height;
            float y = vertex.position.y + graphic.rectTransform.pivot.y * height;
            float t = height > 0f ? Mathf.Clamp01(y / height) : 0f;

            vertex.color = Color.Lerp(bottomColor, topColor, t);
            vertices[i] = vertex;
        }

        vertexHelper.Clear();
        vertexHelper.AddUIVertexTriangleStream(vertices);
    }
}