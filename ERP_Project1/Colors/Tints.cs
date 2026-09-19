using System.Collections.Generic;
using System.Drawing;

namespace FitCore.Models.Colors
{
    public class Tints
    {
        public List<ColorItem> Colors { get; set; }
        public Tints()
        {
            Colors = new List<ColorItem>
            {
                new ColorItem
                {
                    Percentage = 10,
                    Hex = "#E6EDFB",
                    RGB = "230, 237, 251",
                    Color = ColorTranslator.FromHtml("#E6EDFB"),
                    PhysicalColor = "Pale Blue",
                    DigitalColor = "Soft Blue"
                },

                new ColorItem
                {
                    Percentage = 20,
                    Hex = "#CCE0F7",
                    RGB = "204, 224, 247",
                    Color = ColorTranslator.FromHtml("#CCE0F7"),
                    PhysicalColor = "Baby Blue",
                    DigitalColor = "Light Blue"
                },

                new ColorItem
                {
                    Percentage = 30,
                    Hex = "#B3CEF3",
                    RGB = "179, 206, 243",
                    Color = ColorTranslator.FromHtml("#B3CEF3"),
                    PhysicalColor = "Sky Blue",
                    DigitalColor = "Pastel Blue"
                },

                new ColorItem
                {
                    Percentage = 40,
                    Hex = "#99BDF0",
                    RGB = "153, 189, 240",
                    Color = ColorTranslator.FromHtml("#99BDF0"),
                    PhysicalColor = "Powder Blue",
                    DigitalColor = "Cool Blue"
                },

                new ColorItem
                {
                    Percentage = 50,
                    Hex = "#80ACEA",
                    RGB = "128, 172, 234",
                    Color = ColorTranslator.FromHtml("#80ACEA"),
                    PhysicalColor = "Cornflower Blue",
                    DigitalColor = "Bright Blue"
                },

                new ColorItem
                {
                    Percentage = 60,
                    Hex = "#6698E4",
                    RGB = "102, 152, 228",
                    Color = ColorTranslator.FromHtml("#6698E4"),
                    PhysicalColor = "Cerulean Blue",
                    DigitalColor = "Medium Blue"
                },

                new ColorItem
                {
                    Percentage = 70,
                    Hex = "#4D82DE",
                    RGB = "77, 130, 223",
                    Color = ColorTranslator.FromHtml("#4D82DE"),
                    PhysicalColor = "Azure Blue",
                    DigitalColor = "Strong Blue"
                },

                new ColorItem
                {
                    Percentage = 80,
                    Hex = "#336DD9",
                    RGB = "51, 109, 217",
                    Color = ColorTranslator.FromHtml("#336DD9"),
                    PhysicalColor = "Sapphire Blue",
                    DigitalColor = "Vivid Blue"
                },

                new ColorItem
                {
                    Percentage = 90,
                    Hex = "#1A5BD8",
                    RGB = "26, 91, 216",
                    Color = ColorTranslator.FromHtml("#1A5BD8"),
                    PhysicalColor = "Royal Blue",
                    DigitalColor = "Bold Blue"
                },

                new ColorItem
                {
                    Percentage = 100,
                    Hex = "#004FD8",
                    RGB = "0, 79, 216",
                    Color = ColorTranslator.FromHtml("#004FD8"),
                    PhysicalColor = "Deep Blue",
                    DigitalColor = "Classic Blue"
                }
            };
        }
    }


    // Reusable color model
    public class ColorItem
    {
        public int Percentage { get; set; }
        public string Hex { get; set; } = string.Empty;
        public string RGB { get; set; } = string.Empty;
        public Color Color { get; set; }
        public string PhysicalColor { get; set; } = string.Empty;
        public string DigitalColor { get; set; } = string.Empty;
    }
}