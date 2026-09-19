using System.Collections.Generic;
using System.Drawing;

namespace FitCore.Models.Colors
{
	public class Shades
	{
		public List<ColorItem> Colors { get; set; }
		public Shades()
		{
			Colors = new List<ColorItem>
			{
				new ColorItem
				{
					Percentage = 10,
					Hex = "#E5ECF6",
					RGB = "229, 236, 246",
					Color = ColorTranslator.FromHtml("#E5ECF6"),
					PhysicalColor = "Light Blue Gray",
					DigitalColor = "Soft Navy"
				},

				new ColorItem
				{
					Percentage = 20,
					Hex = "#CCD6EB",
					RGB = "204, 214, 235",
					Color = ColorTranslator.FromHtml("#CCD6EB"),
					PhysicalColor = "Slate Blue",
					DigitalColor = "Muted Blue"
				},

				new ColorItem
				{
					Percentage = 30,
					Hex = "#B3C0E0",
					RGB = "179, 192, 224",
					Color = ColorTranslator.FromHtml("#B3C0E0"),
					PhysicalColor = "Steel Blue",
					DigitalColor = "Dusty Blue"
				},

				new ColorItem
				{
					Percentage = 40,
					Hex = "#8CA6D6",
					RGB = "140, 166, 214",
					Color = ColorTranslator.FromHtml("#8CA6D6"),
					PhysicalColor = "Denim Blue",
					DigitalColor = "Deep Blue"
				},

				new ColorItem
				{
					Percentage = 50,
					Hex = "#6690CC",
					RGB = "102, 144, 204",
					Color = ColorTranslator.FromHtml("#6690CC"),
					PhysicalColor = "Ocean Blue",
					DigitalColor = "Classic Blue"
				},

				new ColorItem
				{
					Percentage = 60,
					Hex = "#4C79C2",
					RGB = "76, 121, 194",
					Color = ColorTranslator.FromHtml("#4C79C2"),
					PhysicalColor = "True Blue",
					DigitalColor = "Strong Navy"
				},

				new ColorItem
				{
					Percentage = 70,
					Hex = "#3362B8",
					RGB = "51, 98, 184",
					Color = ColorTranslator.FromHtml("#3362B8"),
					PhysicalColor = "Cobalt Blue",
					DigitalColor = "Royal Navy"
				},

				new ColorItem
				{
					Percentage = 80,
					Hex = "#1F4A9E",
					RGB = "31, 74, 158",
					Color = ColorTranslator.FromHtml("#1F4A9E"),
					PhysicalColor = "Prussian Blue",
					DigitalColor = "Dark Blue"
				},

				new ColorItem
				{
					Percentage = 90,
					Hex = "#102F83",
					RGB = "16, 47, 131",
					Color = ColorTranslator.FromHtml("#102F83"),
					PhysicalColor = "Oxford Blue",
					DigitalColor = "Midnight Blue"
				},

				new ColorItem
				{
					Percentage = 100,
					Hex = "#001A66",
					RGB = "0, 26, 102",
					Color = ColorTranslator.FromHtml("#001A66"),
					PhysicalColor = "Navy Blue",
					DigitalColor = "Ink Blue"
				}
			};
		}
	}
}