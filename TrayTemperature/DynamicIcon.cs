using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TrayTemperature {
	class DynamicIcon {
		//Creates a 16x16 icon with 2 lines of  text
		public static Icon CreateIcon(IList<(string LineText, Color LineColor)> datapoints)
		{
			Font font = new Font("Consolas", 7);
			Bitmap bitmap = new Bitmap(16, 16);

			Graphics graph = Graphics.FromImage(bitmap);

			//Draw the temperatures
			for(var i = 0; i < datapoints.Count; i++)
            {
				var datapoint = datapoints[i];
				graph.DrawString(datapoint.LineText, font, new SolidBrush(datapoint.LineColor), new PointF(-1, -3 + 10 * i));
            }

			return Icon.FromHandle(bitmap.GetHicon());
		}
	}
}
