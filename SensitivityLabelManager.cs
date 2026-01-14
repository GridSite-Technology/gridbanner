using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using System.IO.Compression;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace GridBanner
{
    /// <summary>
    /// Manages sensitivity labels from Office documents and other sources
    /// </summary>
    public class SensitivityLabelManager
    {
        /// <summary>
        /// Gets the sensitivity label from an Office document file
        /// </summary>
        public static SensitivityInfo? GetSensitivityFromOfficeDocument(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                // Check file extension
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".docx" && extension != ".doc" && 
                    extension != ".xlsx" && extension != ".xls" &&
                    extension != ".pptx" && extension != ".ppt")
                {
                    return null;
                }

                // Try to read sensitivity label from document properties
                // Office documents store sensitivity labels in custom properties
                var label = ReadSensitivityFromOfficeFile(filePath);
                
                return label;
            }
            catch (Exception ex)
            {
                LogMessage($"Error reading sensitivity from Office document: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets sensitivity label from currently open Office application
        /// </summary>
        public static SensitivityInfo? GetSensitivityFromActiveOfficeApp(IntPtr windowHandle)
        {
            try
            {
                // Try to get sensitivity label from Office COM interface
                // This requires Office to be installed and COM interop
                var label = ReadSensitivityFromOfficeCOM(windowHandle);
                return label;
            }
            catch (Exception ex)
            {
                LogMessage($"Error reading sensitivity from Office COM: {ex.Message}");
                return null;
            }
        }

        private static SensitivityInfo? ReadSensitivityFromOfficeFile(string filePath)
        {
            try
            {
                // For Office documents, we can try to read from:
                // 1. Document properties (custom properties)
                // 2. Metadata in the file
                
                // Office documents are ZIP files (for .docx, .xlsx, .pptx)
                // Sensitivity labels are stored in custom properties or metadata
                
                // For now, we'll use a simpler approach: check registry/COM
                // A full implementation would need to parse the Office XML structure
                
                return null; // Placeholder - full implementation needed
            }
            catch
            {
                return null;
            }
        }

        private static SensitivityInfo? ReadSensitivityFromOfficeCOM(IntPtr windowHandle)
        {
            try
            {
                // Try to get sensitivity label from Office COM interface
                // Office applications expose SensitivityLabel through COM
                // We'll try Word, Excel, and PowerPoint
                // Use a timeout to prevent hanging
                
                var task = Task.Run(() =>
                {
                    // Try Word first
                    var wordSensitivity = TryGetWordSensitivityLabel();
                    if (wordSensitivity != null)
                    {
                        return wordSensitivity;
                    }
                    
                    // Try Excel
                    var excelSensitivity = TryGetExcelSensitivityLabel();
                    if (excelSensitivity != null)
                    {
                        return excelSensitivity;
                    }
                    
                    // Try PowerPoint
                    var pptSensitivity = TryGetPowerPointSensitivityLabel();
                    if (pptSensitivity != null)
                    {
                        return pptSensitivity;
                    }
                    
                    return null;
                });

                // Wait with timeout (500ms max)
                if (task.Wait(TimeSpan.FromMilliseconds(500)))
                {
                    return task.Result;
                }
                else
                {
                    LogMessage("Office COM check timed out after 500ms");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error reading Office COM sensitivity: {ex.Message}");
                return null;
            }
        }

        private static SensitivityInfo? TryGetWordSensitivityLabel()
        {
            try
            {
                // Get Word instance (will get existing if running, or create new)
                Type? wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType == null)
                {
                    LogMessage("Word.Application ProgID not found - Word may not be installed");
                    return null;
                }

                dynamic? wordApp = null;
                bool shouldRelease = false;
                
                try
                {
                    // CreateInstance will get existing instance if Word is running, or create new
                    wordApp = Activator.CreateInstance(wordType);
                    if (wordApp == null)
                    {
                        LogMessage("Failed to get/create Word instance");
                        return null;
                    }
                    
                    // Check if Word is visible (if not, we probably created a new hidden instance)
                    try
                    {
                        bool isVisible = wordApp.Visible;
                        if (!isVisible)
                        {
                            // We created a new instance, mark for cleanup
                            shouldRelease = true;
                            LogMessage("Created new Word instance (Word was not running)");
                        }
                        else
                        {
                            LogMessage("Got existing Word instance");
                        }
                    }
                    catch
                    {
                        shouldRelease = true; // Assume we created it if we can't check
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Error getting Word instance: {ex.Message}");
                    return null;
                }

                try
                {
                    // Check if there's an active document
                    int docCount = wordApp.Documents.Count;
                    if (docCount == 0)
                    {
                        LogMessage("No documents open in Word");
                        return null;
                    }

                    dynamic? activeDoc = null;
                    try
                    {
                        activeDoc = wordApp.ActiveDocument;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Could not get ActiveDocument: {ex.Message}");
                        // Try getting first document instead
                        if (docCount > 0)
                        {
                            try
                            {
                                activeDoc = wordApp.Documents[1]; // 1-indexed
                            }
                            catch { }
                        }
                    }

                    if (activeDoc == null)
                    {
                        LogMessage("No active document found");
                        return null;
                    }

                    // Try to get SensitivityLabel (Office 365/M365)
                    // Note: SensitivityLabel is a property that may not exist in all Office versions
                    try
                    {
                        // Try direct access first (works in Office 365/M365)
                        try
                        {
                            dynamic? sensitivityLabel = activeDoc.SensitivityLabel;
                            if (sensitivityLabel != null)
                            {
                                try
                                {
                                    var labelId = sensitivityLabel.LabelId;
                                    var labelName = sensitivityLabel.LabelName?.ToString() ?? "";
                                    
                                    // Check if label exists (any label means sensitive)
                                    var labelIdStr = labelId?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(labelIdStr) || !string.IsNullOrEmpty(labelName))
                                    {
                                        LogMessage($"✓ Found sensitivity label via COM: {labelName} (ID: {labelId})");
                                        return new SensitivityInfo
                                        {
                                            Level = SensitivityLevel.Internal,
                                            LabelName = labelName,
                                            Source = "Office Word",
                                            Description = "Office document with sensitivity label"
                                        };
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogMessage($"Error reading sensitivity label properties: {ex.Message}");
                                }
                            }
                            else
                            {
                                LogMessage("SensitivityLabel property is null (no label applied)");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"SensitivityLabel property access failed (may not exist): {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error checking SensitivityLabel: {ex.Message}");
                    }

                    // Try custom document properties as fallback
                    try
                    {
                        dynamic? customProps = activeDoc.CustomDocumentProperties;
                        if (customProps != null)
                        {
                            int propCount = customProps.Count;
                            for (int i = 1; i <= propCount; i++)
                            {
                                try
                                {
                                    dynamic prop = customProps[i];
                                    var propName = prop.Name?.ToString() ?? "";
                                    if (propName.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase) ||
                                        propName.Contains("Label", StringComparison.OrdinalIgnoreCase) ||
                                        propName.Contains("MIP", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var propValue = prop.Value?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(propValue))
                                        {
                                            LogMessage($"Found sensitivity in custom property: {propName}={propValue}");
                                            return new SensitivityInfo
                                            {
                                                Level = SensitivityLevel.Internal,
                                                LabelName = propValue,
                                                Source = "Office Word",
                                                Description = "Office document with sensitivity label"
                                            };
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Custom properties check failed: {ex.Message}");
                    }

                    // Last resort: Check if document has any protection or is from OneDrive/SharePoint
                    // If document path contains sharepoint.com or onedrive.com, consider it sensitive
                    try
                    {
                        string? docPath = activeDoc.FullName?.ToString();
                        if (!string.IsNullOrEmpty(docPath))
                        {
                            LogMessage($"Document path: {docPath}");
                            if (docPath.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
                                docPath.Contains("onedrive.com", StringComparison.OrdinalIgnoreCase) ||
                                docPath.Contains("PrecisionX", StringComparison.OrdinalIgnoreCase))
                            {
                                LogMessage($"✓ Document is from SharePoint/OneDrive or PrecisionX: {docPath}");
                                return new SensitivityInfo
                                {
                                    Level = SensitivityLevel.Internal,
                                    LabelName = "SharePoint/OneDrive/PrecisionX Document",
                                    Source = "Office Word",
                                    Description = "Document from SharePoint, OneDrive, or PrecisionX"
                                };
                            }
                        }
                        else
                        {
                            LogMessage("Document path is empty (may be unsaved document)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error checking document path: {ex.Message}");
                    }
                    
                    // If we get here, no sensitivity was detected
                    LogMessage("No sensitivity indicators found in Word document");
                }
                finally
                {
                    // Only release if we created a new instance
                    // Don't release if we got existing instance (user is using it)
                    if (shouldRelease && wordApp != null)
                    {
                        try
                        {
                            Marshal.ReleaseComObject(wordApp);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting Word sensitivity label: {ex.Message}");
            }
            
            return null;
        }

        private static SensitivityInfo? TryGetExcelSensitivityLabel()
        {
            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    return null;
                }

                dynamic? excelApp = Activator.CreateInstance(excelType);
                if (excelApp == null)
                {
                    return null;
                }

                try
                {
                    if (excelApp.Workbooks.Count == 0)
                    {
                        return null;
                    }

                    dynamic? activeWorkbook = excelApp.ActiveWorkbook;
                    if (activeWorkbook == null)
                    {
                        return null;
                    }

                    try
                    {
                        dynamic? sensitivityLabel = activeWorkbook.SensitivityLabel;
                        if (sensitivityLabel != null)
                        {
                            var labelId = sensitivityLabel.LabelId;
                            var labelName = sensitivityLabel.LabelName?.ToString() ?? "";
                            
                            if (!string.IsNullOrEmpty(labelId?.ToString()) || !string.IsNullOrEmpty(labelName))
                            {
                                return new SensitivityInfo
                                {
                                    Level = SensitivityLevel.Internal,
                                    LabelName = labelName,
                                    Source = "Office Excel",
                                    Description = "Office document with sensitivity label"
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Try custom properties
                        try
                        {
                            dynamic? customProps = activeWorkbook.CustomDocumentProperties;
                            if (customProps != null)
                            {
                                foreach (dynamic prop in customProps)
                                {
                                    var propName = prop.Name?.ToString() ?? "";
                                    if (propName.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase) ||
                                        propName.Contains("Label", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var propValue = prop.Value?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(propValue))
                                        {
                                            return new SensitivityInfo
                                            {
                                                Level = SensitivityLevel.Internal,
                                                LabelName = propValue,
                                                Source = "Office Excel",
                                                Description = "Office document with sensitivity label"
                                            };
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(excelApp);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting Excel sensitivity label: {ex.Message}");
            }
            
            return null;
        }

        private static SensitivityInfo? TryGetPowerPointSensitivityLabel()
        {
            try
            {
                Type? pptType = Type.GetTypeFromProgID("PowerPoint.Application");
                if (pptType == null)
                {
                    return null;
                }

                dynamic? pptApp = Activator.CreateInstance(pptType);
                if (pptApp == null)
                {
                    return null;
                }

                try
                {
                    if (pptApp.Presentations.Count == 0)
                    {
                        return null;
                    }

                    dynamic? activePresentation = pptApp.ActivePresentation;
                    if (activePresentation == null)
                    {
                        return null;
                    }

                    try
                    {
                        dynamic? sensitivityLabel = activePresentation.SensitivityLabel;
                        if (sensitivityLabel != null)
                        {
                            var labelId = sensitivityLabel.LabelId;
                            var labelName = sensitivityLabel.LabelName?.ToString() ?? "";
                            
                            if (!string.IsNullOrEmpty(labelId?.ToString()) || !string.IsNullOrEmpty(labelName))
                            {
                                return new SensitivityInfo
                                {
                                    Level = SensitivityLevel.Internal,
                                    LabelName = labelName,
                                    Source = "Office PowerPoint",
                                    Description = "Office document with sensitivity label"
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Try custom properties
                        try
                        {
                            dynamic? customProps = activePresentation.CustomDocumentProperties;
                            if (customProps != null)
                            {
                                foreach (dynamic prop in customProps)
                                {
                                    var propName = prop.Name?.ToString() ?? "";
                                    if (propName.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase) ||
                                        propName.Contains("Label", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var propValue = prop.Value?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(propValue))
                                        {
                                            return new SensitivityInfo
                                            {
                                                Level = SensitivityLevel.Internal,
                                                LabelName = propValue,
                                                Source = "Office PowerPoint",
                                                Description = "Office document with sensitivity label"
                                            };
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(pptApp);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting PowerPoint sensitivity label: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Gets sensitivity level from a URL/domain
        /// </summary>
        public static SensitivityLevel GetSensitivityFromUrl(string url, SensitivityConfig? config = null)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return SensitivityLevel.None;
            }

            try
            {
                var uri = new Uri(url);
                var domain = uri.Host.ToLowerInvariant();

                // Check for precisionxtech.sharepoint.com or any subdomain
                if (domain.Contains("precisionxtech.sharepoint.com") || 
                    domain.EndsWith(".precisionxtech.sharepoint.com"))
                {
                    return SensitivityLevel.Internal; // Mark as sensitive
                }

                // Check against configured sensitive domains
                if (config != null)
                {
                    foreach (var domainConfig in config.SensitiveDomains)
                    {
                        if (domain.Contains(domainConfig.Domain.ToLowerInvariant()))
                        {
                            return domainConfig.SensitivityLevel;
                        }
                    }
                }

                // Check if it's an internal domain (e.g., .local, .internal, SharePoint)
                if (domain.Contains(".sharepoint.com") || 
                    domain.Contains(".office.com") ||
                    domain.Contains(".microsoft.com") ||
                    domain.Contains(".local") ||
                    domain.Contains(".internal"))
                {
                    return SensitivityLevel.Internal;
                }

                // External/public sites are considered lower sensitivity
                return SensitivityLevel.Public;
            }
            catch
            {
                return SensitivityLevel.None;
            }
        }

        /// <summary>
        /// Checks if a URL is from precisionxtech.sharepoint.com (simplified check)
        /// </summary>
        public static bool IsSharePointUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                var domain = uri.Host.ToLowerInvariant();
                return domain.Contains("precisionxtech.sharepoint.com");
            }
            catch
            {
                return false;
            }
        }

        private static void LogMessage(string message)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "userdata", "gridbanner", "gridbanner.log");
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SensitivityLabelManager] {message}\n");
            }
            catch { }
        }
    }

    public class SensitivityInfo
    {
        public SensitivityLevel Level { get; set; }
        public string LabelName { get; set; } = "";
        public string? Description { get; set; }
        public string Source { get; set; } = ""; // "Office", "SharePoint", "Browser", etc.
    }

    public enum SensitivityLevel
    {
        None = 0,
        Public = 1,
        Internal = 2,
        Confidential = 3,
        Restricted = 4,
        HighlyRestricted = 5
    }
}
