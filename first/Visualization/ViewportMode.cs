namespace first
{
    public enum ViewportMode
    {
        ShadedWireframe, // 3D полигоны с освещением + сетка
        WireframeOnly,   // Каркасная сетка
        PointCloud,      // 3D точки (вершины)
        Heatmap2D        // 2D тепловая карта
    }
}
