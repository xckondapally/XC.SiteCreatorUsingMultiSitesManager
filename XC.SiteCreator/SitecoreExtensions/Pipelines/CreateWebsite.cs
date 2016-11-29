using System;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Web.UI.Pages;
using Sitecore.Web.UI.Sheer;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Sitecore.Web;
using Sitecore.SecurityModel;
using Sitecore.Data.Fields;

namespace XC.SiteCreator.SitecoreExtensions.Pipelines
{
    public class CreateWebsite : DialogForm
    {
        #region Declarations
        private static readonly string siteRootBranchTemplateID = "{04D330F9-7B12-4A65-B4FD-55FDDCDF8F6B}";
        private static readonly string siteDefinitionBranchTemplateID = "{24E0DA61-288B-4872-9D5C-D264230E9A93}";
        private static readonly string sitesParentFolderID = "{67E6AF74-8A3F-4E69-B325-32887B63A25F}";
        private readonly string SiteSettingsTemplateId = "{33A1164D-7E9C-47FF-AA5F-1713A1B6B08E}";
        private static readonly string LanguagesItemId = "{64C4F646-A3FA-4205-B98E-4DE2C609B60F}";
        private static string _sitecoreContentItemId;
        private static Database _database;
        private readonly string[] _delimiter = { "|" };
        protected Edit txtSiteName;
        protected Edit txtLanguages;
        protected Edit txtHostNames;

        #endregion

        #region Processor methods
        /// <summary>
        /// Checks the permissions
        /// </summary>
        /// <param name="args"></param>
        public void CheckPermissions(ClientPipelineArgs args)
        {
            Database database = Factory.GetDatabase(args.Parameters["database"]);
            Assert.IsNotNull((object)database, "datatbase");

            string index = args.Parameters["id"];
            Item contextItem = database.Items[index];

            if (contextItem != null)
            {
                if (contextItem.Access.CanCreate())
                {
                    return;
                }

                Context.ClientPage.ClientResponse.Alert(Translate.Text("You do not have permission to create a site at \"{0}\".", (object)contextItem.DisplayName));
                args.AbortPipeline();
            }
            else
            {
                Context.ClientPage.ClientResponse.Alert("Item not found.");
                args.AbortPipeline();
            }
        }

        /// <summary>
        /// Get the Site Parameters
        /// </summary>
        /// <param name="args"></param>
        public void GetSiteParameters(ClientPipelineArgs args)
        {
            _database = Factory.GetDatabase(args.Parameters["database"]);
            _sitecoreContentItemId = args.Parameters["id"];

            if (args.IsPostBack)
            {
                if (args.HasResult)
                {
                    var newWebsiteRootItem = _database.Items[args.Result];
                    Context.ClientPage.SendMessage(this, "item:load(id=" + (object)newWebsiteRootItem.ID + ")");
                }
            }
            else
            {
                //call the xml control
                var createWebsiteDialogControl = UIUtil.GetUri("control:CreateWebsiteDialog");
                //open dialog form for input from user
                Context.ClientPage.ClientResponse.ShowModalDialog(string.Format("{0}&database={1}", createWebsiteDialogControl, _database), true);
                args.WaitForPostBack();
            }
        }
        #endregion

        #region DialogForm Methods

        /// <summary>
        /// Dialog Form On Load
        /// </summary>
        /// <param name="e"></param>
        protected override void OnLoad(EventArgs e)
        {
            Assert.ArgumentNotNull(e, "e");
            base.OnLoad(e);
        }

        /// <summary>
        /// User clicked OK
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected override void OnOK(object sender, EventArgs args)
        {
            base.OnOK(sender, args);
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(args, "args");
            Item websiteRootItem = null;

            //Get the list of languages
            var listOfLanguages = GetListOfLanguages(txtLanguages.Value);
            Item parentItem = _database.Items[_sitecoreContentItemId];
            Item siteFolderItem = _database.Items[sitesParentFolderID];

            /*Check to make sure if 
             * 1. the culture info for languages provided by user are valid: DoesCultureInfoExist(listOfLanguages)
             * 2. the languages exist in Sitecore under sitecore/system/languages: LanguagesExistInSitecore(listOfLanguages)
            */
            if (DoesCultureInfoExist(listOfLanguages) && parentItem != null)
            {
                if (LanguagesExistInSitecore(listOfLanguages))
                {
                    //Bulk Update context increases the performance of creating items as we update lot of items during the process of Site creation
                    using(new BulkUpdateContext())
                    {
                        //setting the values required for defining the site root under sitecore/content
                        //Sample Path: sitecore/content/Website A
                        websiteRootItem = CreateWebsiteRoot(parentItem, _database);

                        if (websiteRootItem != null)
                        {
                            //setting the values required for defining the Site Definition under sitecore/system/sites
                            //Sample Path: sitecore/system/sites/Website A
                            CreateSiteDefinition(siteFolderItem, _database, listOfLanguages[0], websiteRootItem);

                            //update the site settings with the languages, so that the language selector drop down will populate
                            //Sample Path: sitecore/content/Website A/Site Settings
                            /*Note: This can be commented out in case your site is not multi-lingual*/
                            SetSiteSettingsLanguages(_database, websiteRootItem, listOfLanguages);

                            //add versions in all languages as per user input
                            AddContextLanguageVersions(_database, websiteRootItem, listOfLanguages);
                        }
                    }
                }
                else
                {
                    Context.ClientPage.ClientResponse.Alert(string.Format("One of the language(s) {0} do not exists in Sitecore under system/languages, Please add the language(s) first before creating the website", txtLanguages.Value), null, "Languages Not Added in Sitecore");
                    OnCancel(sender, args);
                }
            }
            else
            {
                Context.ClientPage.ClientResponse.Alert(string.Format("One of the language(s) is not registred, Please register all the language(s) before creating the website"), null, "Culture Code Missing");
                OnCancel(sender, args);
            }

            if (websiteRootItem != null)
            {
                SheerResponse.SetDialogValue(websiteRootItem.ID.ToString());
            }

        }

        #endregion

        #region Create Site related methods

        /// <summary>
        /// Updates the site root to create versions in the primary language, for example: en|es-US for Website A
        /// </summary>
        /// <param name="database"></param>
        /// <param name="webSiteRootItem"></param>
        /// <param name="languagesList"></param>
        private void AddContextLanguageVersions(Database database, Item webSiteRootItem, IEnumerable<string> languagesList)
        {
            if (webSiteRootItem != null)
            {
                foreach (var language in languagesList)
                {
                    AddItemVersions(Language.Parse(language), webSiteRootItem, database);
                }
            }
        }


        /// <summary>
        /// Update the Site Settings with languages for the language selector dropdown
        /// </summary>
        /// <param name="database"></param>
        /// <param name="websiteRootItem"></param>
        /// <param name="languagesList"></param>
        private void SetSiteSettingsLanguages(Database database, Item websiteRootItem, List<string> languagesList)
        {
            var siteSettings = websiteRootItem.Children.FirstOrDefault(x => x.TemplateID.ToString().Equals(SiteSettingsTemplateId));
            var LanguagesItem = database.GetItem(LanguagesItemId);

            if (siteSettings != null)
            {
                List<string> languageIds = new List<string>();

                foreach (var language in languagesList)
                {
                    var languageItem = LanguagesItem.Children.FirstOrDefault(l => l.Name.ToLower().Equals(language.ToLower()));

                    if (languageItem != null)
                    {
                        languageIds.Add(languageItem.ID.ToString());
                    }
                }

                var settingValue = string.Join(_delimiter[0], languageIds);

                using (new SecurityDisabler())
                {
                    siteSettings.Editing.BeginEdit();
                    siteSettings.Fields["Languages"].Value = settingValue;
                    siteSettings.Editing.EndEdit();
                }
            }
        }

        /// <summary>
        /// Creates the Site root using the branch template
        /// </summary>
        /// <param name="parentItem"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        private Item CreateWebsiteRoot(Item parentItem, Database database)
        {
            Item newWebRootItem = null;
            BranchItem siteRootBranchItem = siteRootBranchTemplateID.Length > 0 ? database.Branches[siteRootBranchTemplateID] : database.Branches[TemplateIDs.Folder];

            //Check if branch template exists, else abort pipeline
            if (siteRootBranchItem != null)
            {
                Log.Audit((object)this, "Create Website Root: {0}", new string[1] { AuditFormatter.FormatItem(parentItem) });

                using (new SecurityDisabler())
                {
                    //txtSiteName will replace the $name standard value defined under branch template when the site is created
                    newWebRootItem = siteRootBranchItem.AddTo(parentItem, txtSiteName.Value);
                }
            }
            else
            {
                Context.ClientPage.ClientResponse.Alert(Translate.Text("{0} branch was not found.", (object)siteRootBranchTemplateID));
            }

            return newWebRootItem;
        }

        /// <summary>
        /// Create site definition
        /// </summary>
        /// <param name="parentItem"></param>
        /// <param name="database"></param>
        /// <param name="language"></param>
        /// <param name="websiteRootItem"></param>
        private void CreateSiteDefinition(Item parentItem, Database database, string language, Item websiteRootItem)
        {
            BranchItem siteDefinitionBranchItem = siteDefinitionBranchTemplateID.Length > 0 ? database.Branches[siteDefinitionBranchTemplateID] : database.Branches[TemplateIDs.Folder];

            if (siteDefinitionBranchItem != null)
            {
                Log.Audit((object)this, "Create Site Definition:{0}", new string[1] { AuditFormatter.FormatItem(parentItem) });
                Item newSiteDefinition = siteDefinitionBranchItem.AddTo(parentItem, txtSiteName.Value);

                //Add values based on site root under site defintion
                using (new SecurityDisabler())
                {
                    /*BEGIN EDIT*/
                    newSiteDefinition.Editing.BeginEdit();
                    //setting the hostnames and language
                    newSiteDefinition.Fields["hostName"].Value = txtHostNames.Value;

                    //setting the primary language for the site from the list of languages under system/languages
                    var primaryLanguageItem = database.GetItem(LanguagesItemId).Axes.GetDescendants().FirstOrDefault(x => x.Name.ToLower().Equals(language.ToLower()));

                    if (primaryLanguageItem != null)
                    {
                        ID primaryLanguageId = primaryLanguageItem.ID;
                        newSiteDefinition.Fields["language"].Value = primaryLanguageId.ToString();
                    }

                    /*END EDIT*/
                    newSiteDefinition.Editing.EndEdit();

                    //updating the siteSettings reference
                    var siteSettingsItem = newSiteDefinition.Children.FirstOrDefault(x => x.Name.Equals("siteSettings"));
                    if (siteSettingsItem != null)
                    {
                        siteSettingsItem.Editing.BeginEdit();
                        var siteSettings = websiteRootItem.Children.FirstOrDefault(x => x.TemplateID.ToString().Equals(SiteSettingsTemplateId));

                        if (siteSettings != null)
                        {
                            siteSettingsItem.Fields["Value"].Value = siteSettings.ID.ToString();
                        }

                        siteSettingsItem.Editing.EndEdit();
                    }
                }
            }
            else
            {
                Context.ClientPage.ClientResponse.Alert(Translate.Text("{0} branch was not found.", (object)siteDefinitionBranchTemplateID));
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Create version in the target language and copy all fields from default en language
        /// </summary>
        /// <param name="language"></param>
        /// <param name="rootItem"></param>
        /// <param name="database"></param>
        private void AddItemVersions(Language language, Item rootItem, Database database)
        {
            List<Item> itemAndDescendants = rootItem.Axes.GetDescendants().ToList();
            itemAndDescendants.Add(rootItem);

            foreach (var item in itemAndDescendants)
            {
                Item targetItem = database.Items[item.ID, language];
                Item sourceItem = database.Items[item.ID, Context.Language];

                if (targetItem == null || sourceItem == null || sourceItem.Versions.Count == 0)
                {
                    return;
                }

                using (new SecurityDisabler())
                {
                    try
                    {
                        if (targetItem.Versions.Count == 0)
                        {
                            //add the version if none exist
                            targetItem = targetItem.Versions.AddVersion();
                        }

                        //start editing mode in target language
                        targetItem.Editing.BeginEdit();
                        sourceItem.Fields.ReadAll();

                        foreach (Field field in sourceItem.Fields)
                        {
                            if (field.ID == FieldIDs.FinalLayoutField || (!field.Shared && !field.Name.StartsWith("__") && field.Name.Trim() != string.Empty))
                            {
                                if (field.ID == FieldIDs.FinalLayoutField)
                                {
                                    var finalLayout = LayoutField.GetFieldValue(sourceItem.Fields[FieldIDs.FinalLayoutField]);
                                    LayoutField.SetFieldValue(targetItem.Fields[FieldIDs.FinalLayoutField], finalLayout);
                                }
                                else
                                {
                                    targetItem.Fields[field.Name].SetValue(field.Value, true);
                                }
                            }
                        }

                        //end editing mode
                        targetItem.Editing.EndEdit();
                        targetItem.Editing.AcceptChanges();
                    }
                    catch (Exception e)
                    {
                        targetItem.Editing.CancelEdit();
                        Log.Error(string.Format("There was an exception while editing the Item {0} in {1} language. Message: {2}, More Details: {3}",
                                                    targetItem,
                                                    language,
                                                    e.Message,
                                                    e.StackTrace), this);
                    }
                }
            }
        }

        /// <summary>
        /// Checks to see if the cultureCode is registered
        /// </summary>
        /// <param name="languagList"></param>
        /// <returns>true or false</returns>
        private bool DoesCultureInfoExist(List<string> languagList)
        {
            try
            {

                foreach (var language in languagList)
                {
                    CultureInfo.GetCultureInfo(language);
                }

                return true;
            }
            catch (CultureNotFoundException e)
            {
                Log.Error(string.Format("There was an issue checking for Language CultureCode, Message: {0}, Details: {1}", e.Message, e.StackTrace), this);
            }

            return false;
        }

        /// <summary>
        /// Gets list of languages as provided by the user
        /// </summary>
        /// <param name="languages"></param>
        /// <returns></returns>
        private List<string> GetListOfLanguages(string languages)
        {
            return languages.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>
        /// Checks to make sure if the input language in configured under sitecore/system/languages
        /// </summary>
        /// <returns></returns>
        private bool LanguagesExistInSitecore(IEnumerable<string> languagesList)
        {
            var database = Factory.GetDatabase(WebUtil.GetQueryString("database"));
            Item languagesItem = database.GetItem(LanguagesItemId);

            if (languagesItem != null && languagesItem.HasChildren)
            {
                var listOfLanguagesInSitecore = languagesItem.GetChildren().Select(x => x.Name);

                return !languagesList.Except(listOfLanguagesInSitecore).Any();
            }

            return false;
        }

        #endregion

    }
}
