//   Copyright 2021 Esri
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//   https://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Esri.ArcGISRuntime.Mapping;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Esri.ArcGISRuntime.Geometry;

namespace DisplayAMap
{

    internal class MapViewModel : INotifyPropertyChanged
    {

        public MapViewModel()
        {
            SetupMap();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private Map? _map;
        public Map? Map
        {
            get { return _map; }
            set
            {
                _map = value;
                OnPropertyChanged();
            }
        }

        private void SetupMap()
        {

            // Create a new map with a 'topographic vector' basemap.
            var map = new Map(BasemapStyle.ArcGISTopographic);

            // Set the initial viewpoint around the Santa Monica Mountains in California.
            var mapCenterPoint = new MapPoint(-118.805, 34.027, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(mapCenterPoint, 100000);

            // Set the view model's Map property with the map.
            Map = map;

        }

    }

}

