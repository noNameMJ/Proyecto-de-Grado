using BenchmarkDotNet.Attributes;
using Geomatica.Desktop.ViewModels;
using System.Text.Json;
using Microsoft.VSDiagnostics;

namespace Geomatica.Desktop.Benchmarks
{
    [CPUUsageDiagnoser]
    public class MapaViewModelBenchmark
    {
        private string _polygonGeoJson;
        private string _multiPolygonGeoJson;
        [GlobalSetup]
        public void Setup()
        {
            _polygonGeoJson = @"{
                ""type"": ""Polygon"",
                ""coordinates"": [
                    [
                        [-74.0, 4.0],
                        [-74.1, 4.0],
                        [-74.1, 4.1],
                        [-74.0, 4.1],
                        [-74.0, 4.0]
                    ]
                ]
            }";

            _multiPolygonGeoJson = @"{
                ""type"": ""MultiPolygon"",
                ""coordinates"": [
                    [
                        [
                            [-74.0, 4.0],
                            [-74.1, 4.0],
                            [-74.1, 4.1],
                            [-74.0, 4.1],
                            [-74.0, 4.0]
                        ]
                    ],
                    [
                        [
                            [-75.0, 5.0],
                            [-75.1, 5.0],
                            [-75.1, 5.1],
                            [-75.0, 5.1],
                            [-75.0, 5.0]
                        ]
                    ]
                ]
            }";
        }

        [Benchmark]
        public void ParseGeoJson_Polygon()
        {
            MapaViewModel.ParseGeoJson(_polygonGeoJson);
        }

        [Benchmark]
        public void ParseGeoJson_MultiPolygon()
        {
            MapaViewModel.ParseGeoJson(_multiPolygonGeoJson);
        }
    }
}