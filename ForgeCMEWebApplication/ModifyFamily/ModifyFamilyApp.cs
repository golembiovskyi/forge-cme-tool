using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModifyFamily
{
    public class ModifyFamilyApp : IExternalDBApplication
    {
        string OUTPUT_FILE = "OutputFile.rvt";
        public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
        {
            return ExternalDBApplicationResult.Succeeded;
        }

        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
            return ExternalDBApplicationResult.Succeeded;
        }
        public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
        {
            e.Succeeded = true;
            Document doc= e.DesignAutomationData.RevitDoc;
            if (doc.IsFamilyDocument)
            {
                FamilyManager fm = doc.FamilyManager;
                //Save the updated file by overwriting the existing file
                ModelPath ProjectModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(doc.PathName);
                SaveAsOptions SAO = new SaveAsOptions();
                SAO.OverwriteExistingFile = true;

                //Save the project file with updated window's parameters
                LogTrace("Saving file...");
                doc.SaveAs(ProjectModelPath, SAO);
            }
        }
        private static void LogTrace(string format, params object[] args) { Console.WriteLine(format, args); }
    }
}
