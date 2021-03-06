﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
// jogeurte 11/19

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Xml;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.PowerShell.Commands;
using System.Collections.ObjectModel;

namespace WDAC_Wizard
{
    public partial class SigningRules_Control : UserControl
    {
        // CI Policy objects
        private WDAC_Policy Policy;
        private PolicyCustomRules PolicyCustomRule;     // One instance of a custom rule. Appended to Policy.CustomRules
        private List<string> AllFilesinFolder;          // List to track all files in a folder 

        private Logger Log;
        private MainWindow _MainWindow;
        private string XmlPath;

        private int RowSelected; // Data grid row number selected by the user 

        public SigningRules_Control(MainWindow pMainWindow)
        {
            InitializeComponent();
            this.Policy = new WDAC_Policy();
            this.PolicyCustomRule = new PolicyCustomRules();
            this.AllFilesinFolder = new List<string>(); 

            this._MainWindow = pMainWindow;
            this._MainWindow.RedoFlowRequired = false; 
            this.Log = this._MainWindow.Log;
            this.RowSelected = -1; 
        }

        /// <summary>
        /// Reads in the template or supplemental policy signed file rules and displays them to the user in the DataGridView. 
        /// Executing on UserControl load.
        /// </summary>
        private void SigningRules_Control_Load(object sender, EventArgs e)
        {
            readSetRules();
            displayRules();
        }

        /// <summary>
        /// Shows the Custom Rules Panel when the user clicks on +Custom Rules. 
        /// </summary>
        private void label_AddCustomRules_Click(object sender, EventArgs e)
        {
            if(panel_CustomRules.Visible)
            {
                panel_CustomRules.Visible = false;
                label_AddCustomRules.Text = "+ Custom Rules"; 
            }
            else
            {
                panel_CustomRules.Visible = true;
                comboBox_RuleType.SelectedItem = "Publisher";             // Set as default 
                label_AddCustomRules.Text = "- Custom Rules";
            }
            

            this.Log.AddInfoMsg("--- Create Custom Rules Selected ---"); 
        }

        /// <summary>
        /// Sets the RuleLevel to publisher, filepath or hash for the CustomRules object. 
        /// Executes when user selects the Rule Type dropdown combobox. 
        /// </summary>
        private void RuleType_ComboboxChanged(object sender, EventArgs e)
        {
            string selectedOpt = comboBox_RuleType.SelectedItem.ToString();
            ClearCustomRulesPanel(false);
            label_Info.Visible = true;
            switch(selectedOpt)
            {
                case "Publisher":
                    this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.Publisher);
                    label_Info.Text = "Creates a rule for a file that is signed by the software publisher. \r\n" +
                        "Select a file to use as reference for your rule.";
                    break;

                case "Path":
                    this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.FilePath);
                    label_Info.Text = "Creates a rule for a specific file or folder. \r\n" +
                        "Selecting folder will affect all files in the folder.";
                    panel_FileFolder.Visible = true;
                    radioButton_File.Checked = true; // By default, 
                    break;

                case "File Hash":
                    this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.Hash);
                    label_Info.Text = "Creates a rule for a file that is not signed. \r\n" +
                        "Select the file for which you wish to create a hash rule.";
                    break;
            }

            this.Log.AddInfoMsg(String.Format("Custom File Rule Level Set to {0}", selectedOpt));
        }

        /// <summary>
        /// Clears the remaining UI elements of the Custom Rules Panel when a user selects the 'Create Rule' button. 
        /// /// </summary>
        /// /// <param name="clearComboBox">Bool to reset the Rule Type combobox.</param>
        private void ClearCustomRulesPanel(bool clearComboBox=false)
        {
            // Clear all of UI updates we make based on the type of rule so that the Custom Rules Panel is clear
            //Publisher:
            panel_Publisher_Scroll.Visible = false;
            publisherInfoLabel.Visible = false;
            trackBar_Conditions.ResetText();
            trackBar_Conditions.Value = 0; // default bottom position 

            //File Path:
            panel_FileFolder.Visible = false;

            //Other
            textBox_ReferenceFile.Clear();
            radioButton_Allow.Checked = true;   //default
            label_Info.Visible = false;
            if (clearComboBox)
                comboBox_RuleType.ResetText();
        }

        /// <summary>
        /// Flips the RuleType from Allow to Deny and vice-versa when either radioButton is selected. 
        /// By default, the rules are set to Type=Allow.
        /// </summary>
        private void radioButton_Allow_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_Allow.Checked && !radioButton_Deny.Checked)
                this.PolicyCustomRule.SetRuleType(PolicyCustomRules.RuleType.Allow);

            else
                this.PolicyCustomRule.SetRuleType(PolicyCustomRules.RuleType.Deny);


            this.Log.AddInfoMsg(String.Format("Allow Radio Button set to {0}", 
                this.PolicyCustomRule.GetRuleType() == PolicyCustomRules.RuleType.Allow));
            
        }

        /// <summary>
        /// Flips the PolicyCustom RuleType from FilePath to FolderPath, and vice-versa. 
        /// Prompts user to select another reference path if flipping RuleType after reference
        /// has already been set. 
        /// </summary>
        private void FileButton_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton_File.Checked)
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.FilePath);

            else
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.Folder);

            // Check if user changed Rule Level after already browsing and selecting a reference file
            if (this.PolicyCustomRule.ReferenceFile != null)
                button_Browse_Click(sender, e);

        }


        /// <summary>
        /// Launches the FileDialog and prompts user to select the reference file. 
        /// Based on rule type, sets the UI elements for Publisher, FilePath or Hash rules. 
        /// </summary>
        private void button_Browse_Click(object sender, EventArgs e)
        {
            // Browse button for reference file:
            if(comboBox_RuleType.SelectedItem == null)
            {
                label_Info.Visible = true; 
                label_Info.Text = "Select Rule Type First";
                this.Log.AddWarningMsg("Browse button selected before rule type selected. Set rule type first."); 
                return;
            }
            
            switch (this.PolicyCustomRule.GetRuleLevel())
            {
                case PolicyCustomRules.RuleLevel.Publisher:

                    string refPubPath = getFileLocation();
                    if (refPubPath == String.Empty)
                        break;
                    FileVersionInfo pubFileVersionInfo = FileVersionInfo.GetVersionInfo(refPubPath);
                    
                    // Set Generic Parameters
                    PolicyCustomRule.ReferenceFile = pubFileVersionInfo.FileName; 
                    PolicyCustomRule.FileInfo.Add(pubFileVersionInfo.CompanyName);     
                    PolicyCustomRule.FileInfo.Add(pubFileVersionInfo.ProductName); 
                    PolicyCustomRule.FileInfo.Add(pubFileVersionInfo.OriginalFilename);
                    PolicyCustomRule.FileInfo.Add(pubFileVersionInfo.FileVersion); 

                    // UI
                    textBox_ReferenceFile.Text = PolicyCustomRule.ReferenceFile;
                    textBox_pub_pub.Text = PolicyCustomRule.FileInfo[0];
                    textBox_pub_prodname.Text = PolicyCustomRule.FileInfo[1];
                    textBox_pub_filename.Text = PolicyCustomRule.FileInfo[2];
                    textBox_pub_versionNum.Text = PolicyCustomRule.FileInfo[3];

                    panel_Publisher_Scroll.Visible = true;
                    publisherInfoLabel.Visible = true;
                    publisherInfoLabel.Visible = true; 
                    break;

                case PolicyCustomRules.RuleLevel.Folder:

                    // User wants to create rule by folder level
                    PolicyCustomRule.ReferenceFile = getFolderLocation();
                    this.AllFilesinFolder = new List<string>();
                    if (PolicyCustomRule.ReferenceFile == String.Empty)
                        break;
                    textBox_ReferenceFile.Text = PolicyCustomRule.ReferenceFile;
                    ProcessAllFiles(PolicyCustomRule.ReferenceFile);
                    PolicyCustomRule.FolderContents = this.AllFilesinFolder; 
                    this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.Folder);
                    break; 


                case PolicyCustomRules.RuleLevel.FilePath:
                    
                    // FILE LEVEL
                    string pathObjPath = getFileLocation();
                    if (pathObjPath == String.Empty)
                        break; 

                    FileVersionInfo pathVersionInfo = FileVersionInfo.GetVersionInfo(pathObjPath);

                    // Set Generic Parameters
                    PolicyCustomRule.ReferenceFile = pathVersionInfo.FileName; // pathFileInfo["DOSPath"];             // Filepath
                    PolicyCustomRule.FileInfo.Add(pathVersionInfo.CompanyName);       // publisher // Publisher <-- this one
                    PolicyCustomRule.FileInfo.Add(pathVersionInfo.ProductName); //pu
                    PolicyCustomRule.FileInfo.Add(pathVersionInfo.OriginalFilename); //PolicyCustomRule.VersionNumber = pathFileInfo["FileVersion"];

                    // UI updates
                    radioButton_File.Checked = true;
                    textBox_ReferenceFile.Text = PolicyCustomRule.ReferenceFile;
                    panel_Publisher_Scroll.Visible = false;
                    break;

                case PolicyCustomRules.RuleLevel.Hash:

                    string hashObjPath = getFileLocation();
                    if (hashObjPath == String.Empty)
                        break; 
                    FileVersionInfo hashVersionInfo = FileVersionInfo.GetVersionInfo(hashObjPath);

                    // Set CustomRule Parameters
                    PolicyCustomRule.ReferenceFile = hashVersionInfo.FileName; // pathFileInfo["DOSPath"];             // Filepath
                    PolicyCustomRule.FileInfo.Add(hashVersionInfo.CompanyName);       // publisher // Publisher <-- this one
                    PolicyCustomRule.FileInfo.Add(hashVersionInfo.ProductName); //pu
                    PolicyCustomRule.FileInfo.Add(hashVersionInfo.OriginalFilename); //PolicyCustomRule.VersionNumber = pathFileInfo["FileVersion"];
                    
                    // UI updates
                    panel_Publisher_Scroll.Visible = false;
                    textBox_ReferenceFile.Text = PolicyCustomRule.ReferenceFile;                    
                    break;
            }


        }

        /// <summary>
        /// Opens the file dialog and grabs the file path for PEs only and checks if path exists. 
        /// </summary>
        /// <returns>Returns the full path+name of the file</returns>
        private string getFileLocation()
        {
            //TODO: move these common functions to a separate class
            // Open file dialog to get file or folder path

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = @"C:\Program Files";
            openFileDialog.Title = "Browse for a signed file to use as a reference for the rule.";
            openFileDialog.CheckPathExists = true;
            // Performed scan of program files -- most common filetypes (occurence > 20 in the folder) with SIPs: 
            openFileDialog.Filter = "Portable Executable Files (*.exe; *.dll; *.rll; *.bin)|*.EXE;*.DLL;*.RLL;*.BIN|" +
                "Script Files (*.ps1, *.bat, *.vbs, *.js)|*.PS1;*.BAT;*.VBS, *.JS|" +
                "System Files (*.sys, *.hxs, *.mui, *.lex, *.mof)|*.SYS;*.HXS;*.MUI;*.LEX;*.MOF"; 
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                openFileDialog.Dispose();
                return openFileDialog.FileName;
            }
            else
                return String.Empty;

        }

        /// <summary>
        /// Opens the folder dialog and grabs the folder path. Requires Folder to be toggled when Browse button 
        /// is selected. 
        /// </summary>
        /// <returns>Returns the full path of the folder</returns>
        private string getFolderLocation()
        {
            FolderBrowserDialog openFolderDialog = new FolderBrowserDialog();
            openFolderDialog.Description = "Browse for a folder to use as a reference for the rule.";

            if (openFolderDialog.ShowDialog() == DialogResult.OK)
            {
                openFolderDialog.Dispose();
                return openFolderDialog.SelectedPath;
            }
            else
                return String.Empty;

        }

        /// <summary>
        /// Sets the RuleLevel for the Rule Type=Publisher custom rules and all UI elements when the 
        /// scrollbar location is modified. 
        /// </summary>
        
        private void trackBar_Conditions_Scroll(object sender, EventArgs e)
        {
            int pos = trackBar_Conditions.Value; //Publisher file rules conditions
            
            // Setting the trackBar values snaps the cursor to one of the four options
            if (pos <= 2) 
            {
                // Version + filename + Prodname + publisher -- FileName
                trackBar_Conditions.Value = 0;
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.FileName);
                textBox_pub_versionNum.Text = this.PolicyCustomRule.FileInfo[3];
                publisherInfoLabel.Text = "Rule applies to all files signed by this publisher with this product name, \r\n" +
                    "file name with a version at or above the specified version number.";
                this.Log.AddInfoMsg("Publisher file rule level set to file publisher (0)");
            }
            else if (pos > 2 && pos <= 6) // Prodname + publisher -- Publisher
            {
                // Filename + Prodname + publisher -- FilePublisher
                trackBar_Conditions.Value = 4;
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.FilePublisher);
                textBox_pub_filename.Text = this.PolicyCustomRule.FileInfo[2];
                textBox_pub_versionNum.Text = "*";
                publisherInfoLabel.Text = "Rule applies to all files signed by this publisher with this product name \r\n" +
                    "and this file name.";
                this.Log.AddInfoMsg("Publisher file rule level set to file publisher (4)");
            }
            else if (pos > 6 && pos <= 10 ) // Prodname + publisher -- Publisher
            {
                trackBar_Conditions.Value = 8;
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.Publisher);
                textBox_pub_prodname.Text = this.PolicyCustomRule.FileInfo[1];
                textBox_pub_filename.Text = "*";
                textBox_pub_versionNum.Text = "*";
                publisherInfoLabel.Text = "Rule applies to all files signed by this publisher and with this product name.";
                this.Log.AddInfoMsg("Publisher file rule level set to publisher (8)");
            }
            else //publisher only --  PCA certificate 
            {
                trackBar_Conditions.Value = 12;
                this.PolicyCustomRule.SetRuleLevel(PolicyCustomRules.RuleLevel.PcaCertificate);
                textBox_pub_pub.Text = this.PolicyCustomRule.FileInfo[0];
                textBox_pub_prodname.Text = "*";
                textBox_pub_filename.Text = "*";
                textBox_pub_versionNum.Text = "*";
                publisherInfoLabel.Text = "Rule applies to all files signed by this publisher.";
                this.Log.AddInfoMsg("Publisher file rule level set to PCA certificate (12)");

            }
        }

        /// <summary>
        /// Appends the custom rule to the bottom of the DataGridView and creates the rule in the CustomRules list. 
        /// </summary>
        private void button_Create_Click(object sender, EventArgs e)
        {
            // At a minimum, we need  rule level, and pub/hash/file - defult fallback
            if (!radioButton_Allow.Checked && !radioButton_Deny.Checked || this.PolicyCustomRule.ReferenceFile == null)
            {
                label_Info.Visible = true;
                label_Info.Text = "Please select a rule type, a file and whether to allow or deny.";
                this.Log.AddWarningMsg("Create button rule selected without allow/deny setting and a reference file.");
                return;
            }

            // Add rule and exceptions to the table and master list & Scroll to new row index
            var index = rulesDataGrid.Rows.Add();
            rulesDataGrid.FirstDisplayedScrollingRowIndex = index;

            this.Log.AddInfoMsg("--- New Custom Rule Added ---");

            if (this.PolicyCustomRule.GetRuleType() == PolicyCustomRules.RuleType.Allow)
                rulesDataGrid.Rows[index].Cells["Column_Action"].Value = "Allow";
            else
                rulesDataGrid.Rows[index].Cells["Column_Action"].Value = "Deny";

            string colnameString = String.Empty; 

            switch(this.PolicyCustomRule.GetRuleLevel())
            {
                case PolicyCustomRules.RuleLevel.PcaCertificate:
                    colnameString = String.Format("{0}; ", this.PolicyCustomRule.GetRuleLevel());

                    for (int i = 0; i <= 0; i++)
                        colnameString += String.Format("{0}, ", this.PolicyCustomRule.FileInfo[i]);
                    break;

                case PolicyCustomRules.RuleLevel.Publisher:
                    colnameString = String.Format("{0}; ", this.PolicyCustomRule.GetRuleLevel());

                    for (int i = 0; i <= 1; i++)
                        colnameString += String.Format("{0}, ", this.PolicyCustomRule.FileInfo[i]);
                    break;

                case PolicyCustomRules.RuleLevel.FilePublisher:
                    colnameString = String.Format("{0}; ", this.PolicyCustomRule.GetRuleLevel());
                    for (int i = 0; i <= 2; i++)
                        colnameString += String.Format("{0}, ", this.PolicyCustomRule.FileInfo[i]);
                    break;

                default:
                    colnameString = String.Format("{0}; {1}", this.PolicyCustomRule.GetRuleLevel(), this.PolicyCustomRule.ReferenceFile);
                    break;

            }

            // Add to data table
            rulesDataGrid.Rows[index].Cells["Column_Name"].Value = colnameString;

            if (this.PolicyCustomRule.GetRuleType() == PolicyCustomRules.RuleType.Allow) 
                this.Log.AddInfoMsg(String.Format("ALLOW: {0}", colnameString));
            else
                this.Log.AddInfoMsg(String.Format("DENY: {0}", colnameString));

            // Attach the int row number we added it to
            this.PolicyCustomRule.RowNumber = index; 

            // Add custom list to RulesList
            this.Policy.CustomRules.Add(this.PolicyCustomRule);
            this.PolicyCustomRule = new PolicyCustomRules();
            bubbleUp();

            // Reset UI view
            ClearCustomRulesPanel(true);
        }

        /// <summary>
        /// Diplays the signing rules from the template policy or the supplemental policy in the DataGridView on Control Load. 
        /// </summary>
        private void displayRules()
        {
            int index = 0; 
            // Process publisher rules first:
            foreach (var signingScenario in this.Policy.SigningScenarios)
            {
                foreach (var signerID in signingScenario.Signers)
                {
                    // Get signer attributes
                    string friendlyName = this.Policy.Signers[signerID].Name;
                    string action = this.Policy.Signers[signerID].Action;

                    // Get signer exceptions - if applicable
                    string exceptionList = String.Empty;
                    if (this.Policy.Signers[signerID].Exceptions.Count > 0)
                    {
                        // Iterate through all of the exceptions, get the ID and map to filename
                        foreach (string exceptionID in this.Policy.Signers[signerID].Exceptions)
                        {
                            string exceptionName = this.Policy.FileRules[exceptionID].FileName;
                            exceptionList += String.Format("{0}, ", exceptionName);
                        }
                    }

                    // Get associated/affected files
                    string fileAttrList = String.Empty;
                    if (this.Policy.Signers[signerID].FileAttributes.Count > 0)
                    {
                        // Iterate through all of the exceptions, get the ID and map to filename
                        foreach (string ruleID in this.Policy.Signers[signerID].FileAttributes)
                        {
                            string fileAttrName = this.Policy.FileRules[ruleID].FileName;
                            if (fileAttrName == "*") // applies to all files with ver > min ver
                                fileAttrName = "All files";
                            string minVersion = this.Policy.FileRules[ruleID].MinimumFileVersion;
                            fileAttrList += String.Format("{0} (v{1}+), ", fileAttrName, minVersion);
                        }
                    }

                    // Get row index #, Scroll to new row index
                    index = rulesDataGrid.Rows.Add();

                    // Write to UI
                    rulesDataGrid.Rows[index].Cells["Column_Action"].Value = action;
                    rulesDataGrid.Rows[index].Cells["Column_Level"].Value = "Publisher";
                    rulesDataGrid.Rows[index].Cells["Column_Name"].Value = friendlyName;
                    rulesDataGrid.Rows[index].Cells["Column_Files"].Value = fileAttrList; //.Substring(0, fileAttrList.Length - 1); //trim trailing comma
                    rulesDataGrid.Rows[index].Cells["Column_Exceptions"].Value = exceptionList;
                }

            }

            // Process file rules (hash, file path, file name)
            foreach (var signingScenario in this.Policy.SigningScenarios)
            {
                foreach (var ruleID in signingScenario.FileRules)
                {
                    if(this.Policy.FileRules[ruleID].FriendlyName.Contains("Page") 
                        || this.Policy.FileRules[ruleID].FriendlyName.Contains("Sha256")) // Skip the 3 other hash instances -- no need to show to user (saves time)
                        continue;
                    else
                    {
                        // Get row index #, Scroll to new row index
                        index = rulesDataGrid.Rows.Add();

                        // Write to UI
                        rulesDataGrid.Rows[index].Cells["Column_Action"].Value = this.Policy.FileRules[ruleID].Action;
                        rulesDataGrid.Rows[index].Cells["Column_Level"].Value = this.Policy.FileRules[ruleID].GetRuleType().ToString();
                        if(this.Policy.FileRules[ruleID].GetRuleType() == PolicyFileRules.RuleType.FileName &&
                            this.Policy.FileRules[ruleID].FileName != null)
                            rulesDataGrid.Rows[index].Cells["Column_Name"].Value = this.Policy.FileRules[ruleID].FileName;
                        else
                            rulesDataGrid.Rows[index].Cells["Column_Name"].Value = this.Policy.FileRules[ruleID].FriendlyName;
                    } 
                }
            }

            // Scroll to bottom of table
            rulesDataGrid.FirstDisplayedScrollingRowIndex = index;

        }

        /// <summary>
        /// Method to parse either the template or supplemental policy and store into the custom data structures for digestion. 
        /// </summary>
        private void readSetRules()
        {
            // Always going to have to parse an XML file - either going to be pre-exisiting policy (edit mode, supplmental policy) or template policy (new base)
            if (this._MainWindow.Policy.TemplatePath != null)
                this.XmlPath = this._MainWindow.Policy.TemplatePath;
            else
                this.XmlPath = this._MainWindow.Policy.EditPolicyPath;

            this.Log.AddInfoMsg("--- Reading Set Signing Rules Beginning ---");

            try
            {
               XmlReader xmlReader = new XmlTextReader(this.XmlPath);
                // Counter for end of element nodes
                int eoeCount;

                while (xmlReader.Read())
                {
                    switch (xmlReader.NodeType)
                    {
                        case XmlNodeType.Element:

                            if (xmlReader.IsEmptyElement) // Handle empty elements eg. FileRules and UpdatePolicySigners in NightsWatch
                                break;

                            switch (xmlReader.Name)
                            {
                                case "EKUs":
                                    {
                                        // Handle EKUs - do not show to user, though
                                        eoeCount = 0;
                                        PolicyEKUs policyEKU = new PolicyEKUs();
                                        while (xmlReader.Read() && eoeCount < 1)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    {
                                                        eoeCount = 0;
                                                        policyEKU.ID = xmlReader.GetAttribute("ID");
                                                        policyEKU.Value = xmlReader.GetAttribute("Value");
                                                        policyEKU.FriendlyName = xmlReader.GetAttribute("FriendlyName");
                                                        this.Policy.EKUs.Add(policyEKU);

                                                        this.Log.AddInfoMsg(String.Format("Existing EKU Added - ID: {0}, Value: {1}, Friendly Name: {2}", policyEKU.ID,
                                                            policyEKU.Value, policyEKU.FriendlyName));
                                                    }
                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    break;
                                            }
                                        }

                                    }
                                    break;

                                case "FileRules":
                                    {
                                        // Allow and deny file rules
                                        eoeCount = 0;
                                        PolicyFileRules policyFileRule = new PolicyFileRules();
                                        while (xmlReader.Read() && eoeCount < 1)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    {
                                                        eoeCount = 0;
                                                        // Get the EKU ID and value and add to EKUs dict
                                                        policyFileRule = new PolicyFileRules();
                                                        policyFileRule.Action = xmlReader.Name; //allow or deny
                                                        policyFileRule.ID = xmlReader.GetAttribute("ID");
                                                        policyFileRule.FriendlyName = xmlReader.GetAttribute("FriendlyName");
                                                        policyFileRule.FileName = xmlReader.GetAttribute("FileName");
                                                        policyFileRule.MinimumFileVersion = xmlReader.GetAttribute("MinimumFileVersion");
                                                        policyFileRule.FilePath = xmlReader.GetAttribute("FilePath");
                                                        policyFileRule.Hash = xmlReader.GetAttribute("Hash");
                                                        policyFileRule.SetRuleType();
                                                        
                                                        this.Policy.FileRules[policyFileRule.ID] = policyFileRule; // Add to FileRules dictionary with key set to ruleID
                                                        this.Log.AddInfoMsg(String.Format("Existing File Rule Added - {0} ID: {1}, Friendly Name: {2}," +
                                                            " FileName: {3}, MinVersion: {4}, Path: {5}",
                                                            policyFileRule.Action, policyFileRule.ID, policyFileRule.FriendlyName, policyFileRule.FileName,
                                                            policyFileRule.MinimumFileVersion, policyFileRule.FilePath));
                                                    }
                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    break;

                                                case XmlNodeType.Comment:
                                                    eoeCount++; //break out see the "<!--Signers-->" comment - means we have no filerules
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "Signers":
                                    {
                                        // Store the signers in the policy.Signers dict for easy look up at the time of SigningScenarios, CISigners, UpdatePolicySigners, etc
                                        eoeCount = 0;
                                        PolicySigners policySigner = new PolicySigners();
                                        while (xmlReader.Read() && eoeCount < 2) //2 end elements before new section
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    eoeCount = 0;
                                                    // Get the EKU ID and value and add to EKUs dict
                                                    if (xmlReader.Name == "Signer")
                                                    {
                                                        policySigner = new PolicySigners();
                                                        policySigner.ID = xmlReader.GetAttribute("ID");
                                                        policySigner.Name = xmlReader.GetAttribute("Name");
                                                    }
                                                    else if (xmlReader.Name == "CertRoot")
                                                    {
                                                        policySigner.Type = xmlReader.GetAttribute("Type");
                                                        policySigner.Value = xmlReader.GetAttribute("Value");
                                                    }

                                                    else if (xmlReader.Name == "CertEKU")
                                                        policySigner.CertID = xmlReader.GetAttribute("ID");

                                                    else if (xmlReader.Name == "CertPublisher")
                                                        policySigner.CertPub = xmlReader.GetAttribute("Value");

                                                    else if (xmlReader.Name == "FileAttribRef")
                                                        policySigner.AddFileAttribute(xmlReader.GetAttribute("RuleID"));

                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    if (eoeCount < 2)
                                                    {
                                                        this.Policy.Signers[policySigner.ID] = policySigner;
                                                        this.Log.AddInfoMsg(String.Format("Existing Policy Signer Added - ID: {0},  Name: {1}, Type: {2}, Value: {3}, CertID: {4}, CertPub: {5}",
                                                            policySigner.ID, policySigner.Name, policySigner.Type,
                                                            policySigner.Value, policySigner.CertID, policySigner.CertPub));
                                                    }
                                                        
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "SigningScenarios":
                                    {
                                        eoeCount = 0;
                                        List<string> exceptionList = new List<string>();
                                        string signerID = String.Empty;
                                        PolicySigningScenarios signingScenario = new PolicySigningScenarios();
                                        while (xmlReader.Read() && eoeCount < 4)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    eoeCount = 0;

                                                    switch (xmlReader.Name)
                                                    {
                                                        case "SigningScenario":
                                                            signingScenario = new PolicySigningScenarios();
                                                            signingScenario.ID = xmlReader.GetAttribute("ID");
                                                            signingScenario.Value = xmlReader.GetAttribute("Value");
                                                            signingScenario.FriendlyName = xmlReader.GetAttribute("FriendlyName");

                                                            this.Log.AddInfoMsg(String.Format("Existing Signing Scenario Added - ID: {0}, Value: {1}, FriendlyName: {2}",
                                                            signingScenario.ID, signingScenario.Value, signingScenario.FriendlyName));
                                                            break;
                                                        
                                                        case "AllowedSigner":
                                                            //Get signerID && lookup the PolicySigner by ID
                                                            exceptionList = new List<string>();
                                                            signerID = xmlReader.GetAttribute("SignerId");
                                                            signingScenario.Signers.Add(signerID); 
                                                            this.Policy.Signers[signerID].Action = "Allow";
                                                            this.Policy.Signers[signerID].AddException(exceptionList);
                                                            break;

                                                        case "DeniedSigner":
                                                            //Get signerID
                                                            exceptionList = new List<string>();
                                                            signerID = xmlReader.GetAttribute("SignerId");
                                                            signingScenario.Signers.Add(signerID);
                                                            this.Policy.Signers[signerID].Action = "Deny";
                                                            this.Policy.Signers[signerID].AddException(exceptionList);
                                                            break;

                                                        case "FileRuleRef":
                                                            // This is the case for hash and filepath rules
                                                            string ruleID = xmlReader.GetAttribute("RuleID");
                                                            signingScenario.FileRules.Add(ruleID); 

                                                            break; 

                                                        case "ExceptDenyRule": // parent = last allowedsigner
                                                            exceptionList.Add(xmlReader.GetAttribute("DenyRuleID"));
                                                            break;

                                                        case "ExceptAllowRule":
                                                            exceptionList.Add(xmlReader.GetAttribute("AllowRuleID"));
                                                            break;

                                                    }
                                                    break;

                                                case XmlNodeType.EndElement: //1st time - end of parent allowed signer - every time, end of denyList
                                                    eoeCount++;
                                                    if(!String.IsNullOrEmpty(signerID))
                                                        this.Policy.Signers[signerID].AddException(exceptionList);
                                                    if (eoeCount == 2)
                                                        this.Policy.SigningScenarios.Add(signingScenario);
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "SupplementalPolicySigners":
                                    {
                                        eoeCount = 0;
                                        PolicySupplementalSigners policySupplementalSigners = new PolicySupplementalSigners();
                                        while (xmlReader.Read() && eoeCount < 1)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    eoeCount = 0;
                                                    policySupplementalSigners.SignerId = xmlReader.GetAttribute("SignerId");
                                                    this.Policy.SupplementalSigners.Add(policySupplementalSigners);
                                                    this.Log.AddInfoMsg(String.Format("Existing Supplemental Policy Signer Added: {0}  ", policySupplementalSigners.SignerId));
                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "UpdatePolicySigners":
                                    {
                                        PolicyUpdateSigners policyUpdateSigners = new PolicyUpdateSigners();
                                        eoeCount = 0;
                                        while (eoeCount < 1 && xmlReader.Read())
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:

                                                    eoeCount = 0;
                                                    policyUpdateSigners.SignerId = xmlReader.GetAttribute("SignerId");
                                                    this.Policy.UpdateSigners.Add(policyUpdateSigners);
                                                    this.Log.AddInfoMsg(String.Format("Existing Update Policy Signer Added: {0}  ", policyUpdateSigners.SignerId));
                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "CiSigners":
                                    {
                                        PolicyCISigners policyCISigners = new PolicyCISigners();
                                        eoeCount = 0;
                                        while (xmlReader.Read() && eoeCount < 1)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    eoeCount = 0;
                                                    policyCISigners.SignerId = xmlReader.GetAttribute("SignerId");
                                                    this.Policy.CISigners.Add(policyCISigners);
                                                    this.Log.AddInfoMsg(String.Format("Existing CiSigner Added: {0}  ", policyCISigners.SignerId));
                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "Settings":
                                    {
                                        PolicySettings policySetting = new PolicySettings();
                                        eoeCount = 0;
                                        while (xmlReader.Read() && eoeCount < 3)
                                        {
                                            switch (xmlReader.NodeType)
                                            {
                                                case XmlNodeType.Element:
                                                    eoeCount = 0;
                                                    switch (xmlReader.Name)
                                                    {

                                                        case "Setting":

                                                            policySetting = new PolicySettings();
                                                            policySetting.Provider = xmlReader.GetAttribute("Provider");
                                                            policySetting.Key = xmlReader.GetAttribute("Key");
                                                            policySetting.ValueName = xmlReader.GetAttribute("ValueName");
                                                            break;

                                                        case "String":
                                                            policySetting.ValString = xmlReader.ReadElementContentAsString();
                                                            break;

                                                        case "Boolean":
                                                            policySetting.ValBool = xmlReader.ReadElementContentAsString() == "true";
                                                            break;
                                                    }

                                                    break;

                                                case XmlNodeType.EndElement:
                                                    eoeCount++;
                                                    if (eoeCount == 2)
                                                    {
                                                        this.Policy.PolicySettings.Add(policySetting);
                                                        this.Log.AddInfoMsg(String.Format("Existing Setting Added - Provider: {0},  Key: {1}, Value Name: {2}, String: {3}, Bool: {4}",
                                                            policySetting.Provider, policySetting.Key, policySetting.ValueName,
                                                            policySetting.ValString, policySetting.ValBool));
                                                    }

                                                    break;
                                            }
                                        }
                                    }
                                    break;

                                case "VersionEx":
                                    this.Policy.VersionNumber = xmlReader.ReadElementContentAsString(); //Read in the version ex and set it in the policy object
                                    break; 
                            }
                            break;
                    }
                } //end of while

                xmlReader.Dispose(); 
            } // end of try
            catch (Exception e)
            {
                this.Log.AddErrorMsg("ReadSetRules() has encountered an error: ", e);
            }

            this.Log.AddInfoMsg("--- Reading Set Signing Rules Ending ---");
            
            bubbleUp(); // all original signing rules are set in MainWindow object - ...
                        //all mutations to rules are from here on completed using cmdlets
        }

        /// <summary>
        /// A reccursive function to list all of the PE files in a folder and subfolders to create allow and 
        /// deny rules on folder path rules. Stores filepaths in this.AllFilesinFolder. 
        /// </summary>
        /// <param name="folder">The folder path </param>
        private void ProcessAllFiles(string folder)
        {
            // All extensions we look for
            var ext = new List<string> { ".exe", ".ps1", ".bat", ".vbs", ".js" };
            foreach (var file in Directory.GetFiles(folder,"*.*").Where(s => ext.Contains(Path.GetExtension(s))))
                this.AllFilesinFolder.Add(file);

            // Reccursively grab files from sub dirs
            foreach (string subDir in Directory.GetDirectories(folder))
            {
                try
                {
                    ProcessAllFiles(subDir);
                }
                catch(Exception e)
                {
                    Console.WriteLine(String.Format("Exception found: {0} ", e));
                }
            }

            //PolicyCustomRule.FolderContents = Directory.GetFiles(PolicyCustomRule.ReferenceFile, "*.*", SearchOption.AllDirectories)
        }

        /// <summary>
        /// Method to set all of the MainWindow objects to the local instances of the Policy helper class objects.
        /// </summary>
        private void bubbleUp()
        {
            // Passing rule, signing scenarios, etc datastructs to MainWindow class
           this._MainWindow.Policy.CISigners = this.Policy.CISigners;
           this._MainWindow.Policy.EKUs = this.Policy.EKUs;
           this._MainWindow.Policy.FileRules = this.Policy.FileRules;
           this._MainWindow.Policy.Signers = this.Policy.Signers;
           this._MainWindow.Policy.SigningScenarios = this.Policy.SigningScenarios;
           this._MainWindow.Policy.UpdateSigners = this.Policy.UpdateSigners;
           this._MainWindow.Policy.SupplementalSigners = this.Policy.SupplementalSigners;
           this._MainWindow.Policy.CISigners = this.Policy.CISigners;
           this._MainWindow.Policy.PolicySettings = this.Policy.PolicySettings;
           this._MainWindow.Policy.CustomRules = this.Policy.CustomRules;

        }

        /// <summary>
        /// Removes the highlighted rule row in the this.rulesDataGrid DataGridView. 
        /// Can only be executed on custom rules from this session. 
        /// </summary>
        private void deleteButton_Click(object sender, EventArgs e)
        {
            // Get info about the rule user wants to delete: row index and value
            int rowIdx = this.rulesDataGrid.CurrentCell.RowIndex;
            string ruleName = (String)this.rulesDataGrid["Column_Name", rowIdx].Value;
            string ruleType = (String)this.rulesDataGrid["Column_Level", rowIdx].Value; 

            // Prompt the user for additional deletion confirmation
            DialogResult res = MessageBox.Show(String.Format("Are you sure you want to delete the '{0}' rule?", ruleName), "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (res == DialogResult.Yes)
            {
                var ruleIDs = new List<string>();

                // Convert ruleName to SignerID to delete from //Signers and //SigningScenarios
                // Handle both Signer template rules and custom rules
                Dictionary<string, PolicySigners>.KeyCollection _keys = this.Policy.Signers.Keys;
                Dictionary<string, PolicyFileRules>.KeyCollection _fileRulekeys = this.Policy.FileRules.Keys;

                if (ruleType == "Publisher")
                {
                    foreach (var key in _keys)
                    {
                        var pSigner = this.Policy.Signers[key];
                        if (pSigner.Name == ruleName)
                            ruleIDs.Add(key);
                    }
                }
                
                else
                {
                    // Get list of IDs for related rules. ie. Deleting one hash should delete all four hash values.
                    if (ruleType == "Hash")
                        ruleName = ruleName.Substring(0, ruleName.IndexOf("Hash") - 1); 

                    foreach (var key in _fileRulekeys)
                    {
                        var pFileRule = this.Policy.FileRules[key];
                        if (pFileRule.FriendlyName.Contains(ruleName))
                            ruleIDs.Add(key);
                    }
                }
                

                // Only structure we have to remove the rule from is the one that is used in writing rules -- custom rules
                for (int i = this.Policy.CustomRules.Count - 1; i>=0; i--)
                {
                    if (this.Policy.CustomRules[i].RowNumber == rowIdx)
                        this.Policy.CustomRules.Remove(this.Policy.CustomRules[i]); // Remove from structs
                }

                // Try to delete the rule from the doc
                XmlDocument doc = new XmlDocument();
                doc.Load(this.XmlPath); // Reading from the template (either one of the 3 bases or editing)

                
                // A friendly name can have multiple references in the doc -- remove each one
                // Skips section if custom rule
                if (ruleType == "Publisher")
                {
                    // Signer specific
                    XmlNodeList signerNodeList = doc.GetElementsByTagName("Signer");
                    XmlNodeList signingScenarioAllowList = doc.GetElementsByTagName("AllowedSigner");
                    XmlNodeList signingScenarioDenyList = doc.GetElementsByTagName("DeniedSigner");
                    foreach (var ruleID in ruleIDs)
                    {
                        for (int i = signerNodeList.Count - 1; i >= 0; i--) // Traverse through xml elements and delete signers == ruleID
                        {
                            if (signerNodeList[i].OuterXml.Contains(ruleID))
                                signerNodeList[i].ParentNode.RemoveChild(signerNodeList[i]);
                        }

                        for (int i = signingScenarioAllowList.Count - 1; i >= 0; i--) // Remove from signing scenarios too
                        {
                            if (signingScenarioAllowList[i].OuterXml.Contains(ruleID))
                                signingScenarioAllowList[i].ParentNode.RemoveChild(signingScenarioAllowList[i]);
                        }

                        for (int i = signingScenarioDenyList.Count - 1; i >= 0; i--) // Remove from signing scenarios too
                        {
                            if (signingScenarioDenyList[i].OuterXml.Contains(ruleID))
                                signingScenarioDenyList[i].ParentNode.RemoveChild(signingScenarioDenyList[i]);
                        }

                    }
                }

                else
                {
                    // Filerule specific
                    XmlNodeList allowFileRuleNodeList = doc.GetElementsByTagName("Allow"); // in <FileRules>
                    XmlNodeList denyFileRuleNodeList = doc.GetElementsByTagName("Deny");   // in <FileRules>
                    XmlNodeList fileAttrNodeList = doc.GetElementsByTagName("FileRuleRef"); // in <SigningScnearios-->FileRulesRef>
                    foreach (var ruleID in ruleIDs)
                    {
                        for (int i = allowFileRuleNodeList.Count - 1; i >= 0; i--) // Traverse through xml elements and delete signers == ruleID
                        {
                            if (allowFileRuleNodeList[i].OuterXml.Contains(ruleID))
                                allowFileRuleNodeList[i].ParentNode.RemoveChild(allowFileRuleNodeList[i]);
                        }

                        for (int i = denyFileRuleNodeList.Count - 1; i >= 0; i--) // Remove from file rule
                        {
                            if (denyFileRuleNodeList[i].OuterXml.Contains(ruleID))
                                denyFileRuleNodeList[i].ParentNode.RemoveChild(denyFileRuleNodeList[i]);
                        }

                        for (int i = fileAttrNodeList.Count - 1; i >= 0; i--) // Remove from signing scenarios too
                        {
                            if (fileAttrNodeList[i].OuterXml.Contains(ruleID))
                                fileAttrNodeList[i].ParentNode.RemoveChild(fileAttrNodeList[i]);
                        }

                    }
                }

                // Delete from UI elements:
                this.rulesDataGrid.Rows.RemoveAt(rowIdx);
                doc.Save(this.XmlPath); 
            }
             
        }

        /// <summary>
        /// Highlights the row of data in the DataGridView
        /// </summary>
        private void DataClicked(object sender, DataGridViewCellEventArgs e)
        {
            // Remove highlighting from previous selected row
            DataGridViewCellStyle defaultCellStyle = new DataGridViewCellStyle();
            defaultCellStyle.BackColor = Color.White;
            if(this.RowSelected > 0 && this.RowSelected < this.rulesDataGrid.Rows.Count)
                this.rulesDataGrid.Rows[this.RowSelected].DefaultCellStyle = defaultCellStyle; 

            // Highlight the row to show user feedback
            DataGridViewCellStyle highlightCellStyle = new DataGridViewCellStyle();
            highlightCellStyle.BackColor = Color.FromArgb(0, 120, 215); 
            DataGridViewRow customRow = this.rulesDataGrid.CurrentRow;
            this.rulesDataGrid.Rows[customRow.Index].DefaultCellStyle = highlightCellStyle;
            this.RowSelected = customRow.Index; 
            
        }
    }

}
