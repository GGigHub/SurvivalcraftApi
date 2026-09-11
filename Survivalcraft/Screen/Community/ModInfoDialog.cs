using System.Globalization;
using System.Xml.Linq;

namespace Game
{
    public sealed class ModInfoDialog : Dialog
    {
        public ModInfoDialog(ModInfo modInfo, string fallbackVersion, string fallbackLink)
        {
            string valueOrUnknown(string value) => string.IsNullOrWhiteSpace(value)
                ? LanguageControl.Unknown
                : value.Trim();

            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusModInfoDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.NameLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "ModName"), valueOrUnknown(modInfo?.Name));
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.VersionLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "ModVersion"), valueOrUnknown(modInfo?.Version ?? fallbackVersion));
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.GameVersionLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "GameVersion"), valueOrUnknown(modInfo?.ScVersion));
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.ApiVersionLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "ApiVersion"), valueOrUnknown(modInfo?.ApiVersion));
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.AuthorLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "ModAuthor"), valueOrUnknown(modInfo?.Author));
            Children.Find<LabelWidget>("ScbbsPlusModInfoDialog.DescriptionLabel").Text =
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusMoreScreen", "ModDescription"), valueOrUnknown(modInfo?.Description));

            string projectUrl = string.IsNullOrWhiteSpace(modInfo?.Link) ? fallbackLink : modInfo.Link.Trim();
            LinkWidget projectLink = Children.Find<LinkWidget>("ScbbsPlusModInfoDialog.ProjectLink");
            projectLink.Text = valueOrUnknown(projectUrl);
            projectLink.Url = string.IsNullOrWhiteSpace(projectUrl) ? null : projectUrl;
        }

        public override void Update()
        {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
            }
        }
    }
}
