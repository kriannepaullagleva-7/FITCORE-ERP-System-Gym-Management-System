using System.Collections.Generic;
using System.Drawing;

namespace FitCore.Models.Colors
{
    public class Tones
    {
        public List<ColorItem> Colors { get; set; }
        public Tones()
        {
            Colors = new List<ColorItem>
            {
                new ColorItem
                {
                    Percentage = 10,
                    Hex = "#B3C6E6",
                    RGB = "179, 198, 230",
                    Color = ColorTranslator.FromHtml("#B3C6E6"),
                    PhysicalColor = "Grayish Blue",
                    DigitalColor = "Muted Blue"
                },

                new ColorItem
                {
                    Percentage = 20,
                    Hex = "#99AED1",
                    RGB = "153, 174, 209",
                    Color = ColorTranslator.FromHtml("#99AED1"),
                    PhysicalColor = "Blue Gray",
                    DigitalColor = "Soft Steel Blue"
                },

                new ColorItem
                {
                    Percentage = 30,
                    Hex = "#8096BC",
                    RGB = "128, 150, 188",
                    Color = ColorTranslator.FromHtml("#8096BC"),
                    PhysicalColor = "Slate Blue",
                    DigitalColor = "Gray Blue"
                },

                new ColorItem
                {
                    Percentage = 40,
                    Hex = "#6B83A8",
                    RGB = "107, 131, 168",
                    Color = ColorTranslator.FromHtml("#6B83A8"),
                    PhysicalColor = "Smoky Blue",
                    DigitalColor = "Cool Slate"
                },

                new ColorItem
                {
                    Percentage = 50,
                    Hex = "#4C6A94",
                    RGB = "76, 106, 148",
                    Color = ColorTranslator.FromHtml("#4C6A94"),
                    PhysicalColor = "Dusk Blue",
                    DigitalColor = "Storm Blue"
                },

                new ColorItem
                {
                    Percentage = 60,
                    Hex = "#35527F",
                    RGB = "53, 82, 127",
                    Color = ColorTranslator.FromHtml("#35527F"),
                    PhysicalColor = "Twilight Blue",
                    DigitalColor = "Evening Blue"
                },

                new ColorItem
                {
                    Percentage = 70,
                    Hex = "#1F3B68",
                    RGB = "31, 59, 104",
                    Color = ColorTranslator.FromHtml("#1F3B68"),
                    PhysicalColor = "Midnight Blue",
                    DigitalColor = "Dark Slate Blue"
                },

                new ColorItem
                {
                    Percentage = 80,
                    Hex = "#0F294F",
                    RGB = "15, 41, 79",
                    Color = ColorTranslator.FromHtml("#0F294F"),
                    PhysicalColor = "Deep Navy",
                    DigitalColor = "Rich Navy"
                },

                new ColorItem
                {
                    Percentage = 90,
                    Hex = "#061A36",
                    RGB = "6, 26, 54",
                    Color = ColorTranslator.FromHtml("#061A36"),
                    PhysicalColor = "Ink Blue",
                    DigitalColor = "Deep Blue"
                },

                new ColorItem
                {
                    Percentage = 100,
                    Hex = "#004FD8",
                    RGB = "0, 79, 216",
                    Color = ColorTranslator.FromHtml("#004FD8"),
                    PhysicalColor = "True Blue",
                    DigitalColor = "Classic Blue"
                }
            };
        }
    }
}