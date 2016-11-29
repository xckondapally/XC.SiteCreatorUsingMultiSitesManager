using Sitecore.Data.Items;
using Sitecore.Shell.Framework.Commands;
using Sitecore.Diagnostics;
using System.Collections.Specialized;
using Sitecore;

namespace XC.SiteCreator.SitecoreExtensions.Commands
{
    public class CreateWebsiteCommand : Command
    {
        #region Command overrides

        /// <summary>
        /// Execute Command
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(CommandContext context)
        {
            if (context.Items.Length != 1)
            {
                return;
            }

            Item contextItem = context.Items[0];
            InitializePipeline("createWebsite", contextItem);
        }

        /// <summary>
        /// Queries the state of the command.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>
        /// The state of the command.
        /// </returns>
        /// <contract><requires name="context" condition="not null"/></contract>
        public override CommandState QueryState(CommandContext context)
        {
            Assert.ArgumentNotNull((object)context, "context");

            if (context.Items.Length != 1)
            {
                return CommandState.Disabled;
            }

            Item obj = context.Items[0];

            if (!obj.Access.CanCreate() || !obj.Access.CanWriteLanguage())
            {
                return CommandState.Disabled;
            }
            else
            {
                return base.QueryState(context);
            }

        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Initiates the pipeline to create Site
        /// </summary>
        /// <param name="pipelineName"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private static NameValueCollection InitializePipeline(string pipeline, Item item)
        {
            Assert.ArgumentNotNullOrEmpty(pipeline, "pipeline");
            Assert.ArgumentNotNull((object)item, "item");

            var parameters = new NameValueCollection
            {
                {"id", item.ID.ToString()},
                {"database", item.Database.Name},
                {"language", item.Language.ToString()},
                {"version", item.Version.ToString()}
            };

            Context.ClientPage.Start(pipeline, parameters);
            return parameters;
        }

        #endregion
    }
}
