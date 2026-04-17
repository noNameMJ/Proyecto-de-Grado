```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.22621.4317/22H2/2022Update/SunValley2)
Intel Xeon E-2136 CPU 3.30GHz (Max: 3.31GHz), 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 8.0.26 (8.0.2626.16921), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.26 (8.0.2626.16921), X64 RyuJIT AVX2


```
| Method                    | Mean      | Error     | StdDev    |
|-------------------------- |----------:|----------:|----------:|
| ParseGeoJson_Polygon      |  6.690 μs | 0.0640 μs | 0.0598 μs |
| ParseGeoJson_MultiPolygon | 11.081 μs | 0.1263 μs | 0.0986 μs |
