using Xunit;

// Deshabilita la ejecución paralela de pruebas para evitar condiciones de carrera
// sobre el singleton de WPF Application.Current, Dispatcher y el entorno nativo de ArcGIS / GDAL.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]

