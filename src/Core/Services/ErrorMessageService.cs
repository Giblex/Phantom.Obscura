using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Provides comprehensive, user-friendly error messages for all PhantomVault operations.
    /// Includes troubleshooting guidance and actionable steps for resolution.
    /// </summary>
    public static class ErrorMessageService
    {
        private static readonly Dictionary<string, ErrorMessageInfo> _errorMessages = new()
        {
            // Authentication Errors
            ["AUTH_INVALID_PASSWORD"] = new()
            {
                Title = "Incorrect Password",
                Message = "The password you entered is incorrect.",
                TroubleshootingSteps = new[]
                {
                    "Verify Caps Lock is not enabled",
                    "Check if you're using the correct keyboard layout",
                    "Try the recovery codes if you've forgotten your password",
                    "Contact support if you're locked out of your vault"
                },
                Severity = ErrorSeverity.Medium
            },

            ["AUTH_INVALID_PIN"] = new()
            {
                Title = "Incorrect PIN",
                Message = "The PIN you entered is incorrect.",
                TroubleshootingSteps = new[]
                {
                    "Remember: PIN is case-sensitive and numeric",
                    "Check for accidental Num Lock issues",
                    "Use passphrase fallback if you've forgotten your PIN",
                    $"After {5} failed attempts, you'll need to re-authenticate with your main password"
                },
                Severity = ErrorSeverity.Medium
            },

            ["AUTH_THROTTLED"] = new()
            {
                Title = "Too Many Failed Attempts",
                Message = "You've exceeded the maximum number of failed authentication attempts. Please wait before trying again.",
                TroubleshootingSteps = new[]
                {
                    "Wait for the timeout period to expire",
                    "Use recovery codes to reset your authentication",
                    "Contact support if you believe this is an error"
                },
                Severity = ErrorSeverity.High
            },

            ["AUTH_KEYFILE_NOT_FOUND"] = new()
            {
                Title = "Keyfile Not Found",
                Message = "The required keyfile could not be found at the specified location.",
                TroubleshootingSteps = new[]
                {
                    "Verify the keyfile exists on your USB drive or designated location",
                    "Check that the USB drive is properly connected",
                    "Ensure you're using the correct keyfile (not a copy or modified version)",
                    "Restore the keyfile from a backup if necessary"
                },
                Severity = ErrorSeverity.High
            },

            // USB/Storage Errors
            ["USB_NOT_FOUND"] = new()
            {
                Title = "USB Device Not Found",
                Message = "The required USB device is not connected or could not be detected.",
                TroubleshootingSteps = new[]
                {
                    "Connect the USB drive that contains your vault",
                    "Try a different USB port",
                    "Check if the USB drive is recognized by your operating system",
                    "Ensure the USB drive is not write-protected",
                    "Verify the USB drive serial number matches your vault configuration"
                },
                Severity = ErrorSeverity.High
            },

            ["USB_WRONG_DEVICE"] = new()
            {
                Title = "Incorrect USB Device",
                Message = "The connected USB device does not match the required device serial number or volume label.",
                TroubleshootingSteps = new[]
                {
                    "Connect the correct USB drive associated with this vault",
                    "Verify the volume label matches your vault configuration",
                    "Check USB device serial number in vault settings",
                    "Contact support if you need to migrate your vault to a new USB drive"
                },
                Severity = ErrorSeverity.High
            },

            ["USB_REMOVED_DURING_OPERATION"] = new()
            {
                Title = "USB Device Removed",
                Message = "The USB device was unexpectedly removed during an operation. Your vault has been locked for security.",
                TroubleshootingSteps = new[]
                {
                    "Reconnect the USB drive",
                    "Unlock your vault with your credentials",
                    "Verify no data was corrupted by checking recent entries",
                    "Enable USB removal protection in settings to prevent accidental disconnection"
                },
                Severity = ErrorSeverity.Critical
            },

            // Encryption Errors
            ["CRYPTO_AUTHENTICATION_TAG_MISMATCH"] = new()
            {
                Title = "Data Authentication Failed",
                Message = "The encrypted data could not be authenticated. This may indicate data corruption or tampering.",
                TroubleshootingSteps = new[]
                {
                    "Do not proceed - your data may have been tampered with",
                    "Restore from a known good backup",
                    "Run data integrity verification",
                    "Check for disk errors on your storage device",
                    "Contact support immediately if tampering is suspected"
                },
                Severity = ErrorSeverity.Critical
            },

            ["CRYPTO_DECRYPTION_FAILED"] = new()
            {
                Title = "Decryption Failed",
                Message = "Failed to decrypt vault data. This may be due to incorrect credentials or data corruption.",
                TroubleshootingSteps = new[]
                {
                    "Verify you're using the correct password and keyfile",
                    "Check for USB device issues if vault is USB-bound",
                    "Try restoring from a recent backup",
                    "Run filesystem check on your vault drive",
                    "Contact support with vault diagnostics"
                },
                Severity = ErrorSeverity.Critical
            },

            ["CRYPTO_KEY_DERIVATION_FAILED"] = new()
            {
                Title = "Key Derivation Error",
                Message = "Failed to derive encryption keys from your credentials. This is a critical security function.",
                TroubleshootingSteps = new[]
                {
                    "Ensure sufficient system memory is available (Argon2id requires significant RAM)",
                    "Close other memory-intensive applications",
                    "Restart PhantomVault",
                    "Check system logs for memory errors",
                    "Contact support if the issue persists"
                },
                Severity = ErrorSeverity.Critical
            },

            // Vault Errors
            ["VAULT_CORRUPTED"] = new()
            {
                Title = "Vault Data Corrupted",
                Message = "The vault database appears to be corrupted and cannot be loaded.",
                TroubleshootingSteps = new[]
                {
                    "Do not make any changes - corruption may be recoverable",
                    "Restore from the most recent backup",
                    "Use vault recovery tool to attempt data extraction",
                    "Check disk health (SMART status) for hardware issues",
                    "Contact support with vault diagnostics for professional recovery"
                },
                Severity = ErrorSeverity.Critical
            },

            ["VAULT_VERSION_MISMATCH"] = new()
            {
                Title = "Vault Version Incompatible",
                Message = "This vault was created with a different version of PhantomVault and cannot be opened.",
                TroubleshootingSteps = new[]
                {
                    "Check the required PhantomVault version in vault properties",
                    "Update PhantomVault to the latest version",
                    "Or downgrade to the version that created this vault",
                    "Export data from the original version and re-import if needed",
                    "Contact support for vault migration assistance"
                },
                Severity = ErrorSeverity.High
            },

            ["VAULT_LOCKED_BY_POLICY"] = new()
            {
                Title = "Vault Locked by Security Policy",
                Message = "A security policy violation has occurred and your vault has been locked.",
                TroubleshootingSteps = new[]
                {
                    "Review security policy violations in the audit log",
                    "Resolve the policy issue (e.g., reconnect USB, disable debugger)",
                    "Unlock vault with full authentication",
                    "Contact your administrator if this is an enterprise vault"
                },
                Severity = ErrorSeverity.High
            },

            // VeraCrypt Errors
            ["VERACRYPT_NOT_FOUND"] = new()
            {
                Title = "VeraCrypt Not Installed",
                Message = "VeraCrypt is required for container operations but could not be found on your system.",
                TroubleshootingSteps = new[]
                {
                    "Download and install VeraCrypt from: https://www.veracrypt.fr",
                    "Use the built-in VeraCrypt installer in PhantomVault settings",
                    "Restart PhantomVault after installing VeraCrypt",
                    "Ensure VeraCrypt is installed in the default location"
                },
                Severity = ErrorSeverity.High
            },

            ["VERACRYPT_MOUNT_FAILED"] = new()
            {
                Title = "Container Mount Failed",
                Message = "Failed to mount the VeraCrypt container. The container may be in use or credentials are incorrect.",
                TroubleshootingSteps = new[]
                {
                    "Verify the container file exists and is not corrupted",
                    "Check that no other application has the container open",
                    "Ensure correct password/keyfile are being used",
                    "Try mounting the container manually with VeraCrypt to test",
                    "Check available drive letters (Windows) or mount points (Linux/Mac)"
                },
                Severity = ErrorSeverity.High
            },

            // Import/Export Errors
            ["IMPORT_UNSUPPORTED_FORMAT"] = new()
            {
                Title = "Unsupported Import Format",
                Message = "The selected file format is not supported for import.",
                TroubleshootingSteps = new[]
                {
                    "Supported formats: CSV, JSON, KeePass (.kdbx), 1Password, LastPass",
                    "Export your data from the source app in a compatible format",
                    "Check the import template documentation for format requirements",
                    "Contact support if you need help converting your data"
                },
                Severity = ErrorSeverity.Medium
            },

            ["IMPORT_VALIDATION_FAILED"] = new()
            {
                Title = "Import Data Validation Failed",
                Message = "Some entries in the import file failed validation and could not be imported.",
                TroubleshootingSteps = new[]
                {
                    "Review the detailed validation errors in the import log",
                    "Fix formatting issues in your import file",
                    "Remove or correct invalid entries",
                    "Use the import template for proper formatting examples"
                },
                Severity = ErrorSeverity.Medium
            },

            // Policy Errors
            ["POLICY_SIGNATURE_INVALID"] = new()
            {
                Title = "Invalid Policy Signature",
                Message = "The security policy signature could not be verified. This may indicate tampering.",
                TroubleshootingSteps = new[]
                {
                    "Do not proceed with an unsigned or invalidly signed policy",
                    "Restore the policy file from a trusted source",
                    "Verify the policy file has not been modified",
                    "Contact your administrator for a properly signed policy",
                    "Check for man-in-the-middle attacks on your network"
                },
                Severity = ErrorSeverity.Critical
            },

            ["POLICY_VIOLATION"] = new()
            {
                Title = "Security Policy Violation",
                Message = "An action was blocked due to a security policy violation.",
                TroubleshootingSteps = new[]
                {
                    "Review the specific policy requirement that was violated",
                    "Ensure all security requirements are met (USB, MFA, etc.)",
                    "Contact your administrator if you need policy adjustments",
                    "Check audit logs for detailed policy violation information"
                },
                Severity = ErrorSeverity.High
            },

            // System Errors
            ["SYSTEM_INSUFFICIENT_MEMORY"] = new()
            {
                Title = "Insufficient Memory",
                Message = "The system does not have enough available memory to complete this operation.",
                TroubleshootingSteps = new[]
                {
                    "Close other applications to free up memory",
                    "Restart your computer",
                    "Consider upgrading RAM if this occurs frequently",
                    "Reduce Argon2id memory parameter in advanced settings (reduces security)"
                },
                Severity = ErrorSeverity.High
            },

            ["SYSTEM_PERMISSION_DENIED"] = new()
            {
                Title = "Permission Denied",
                Message = "PhantomVault does not have permission to access the required file or folder.",
                TroubleshootingSteps = new[]
                {
                    "Run PhantomVault with administrator/root privileges",
                    "Check file and folder permissions",
                    "Ensure antivirus is not blocking PhantomVault",
                    "Move vault to a location where you have full permissions"
                },
                Severity = ErrorSeverity.High
            }
        };

        /// <summary>
        /// Gets a comprehensive error message with troubleshooting steps.
        /// </summary>
        public static ErrorMessageInfo GetErrorMessage(string errorCode, params object[] formatArgs)
        {
            if (_errorMessages.TryGetValue(errorCode, out var errorInfo))
            {
                return new ErrorMessageInfo
                {
                    Title = errorInfo.Title,
                    Message = formatArgs.Length > 0 ? string.Format(errorInfo.Message, formatArgs) : errorInfo.Message,
                    TroubleshootingSteps = errorInfo.TroubleshootingSteps,
                    Severity = errorInfo.Severity,
                    ErrorCode = errorCode
                };
            }

            return new ErrorMessageInfo
            {
                Title = "Unknown Error",
                Message = $"An unexpected error occurred (Code: {errorCode}). Please contact support.",
                TroubleshootingSteps = new[]
                {
                    "Check the application logs for detailed error information",
                    "Restart PhantomVault",
                    "Contact support with the error code and logs"
                },
                Severity = ErrorSeverity.High,
                ErrorCode = errorCode
            };
        }

        /// <summary>
        /// Gets an error message from an exception.
        /// </summary>
        public static ErrorMessageInfo GetErrorMessageFromException(Exception ex)
        {
            return ex switch
            {
                CryptographicException when ex.Message.Contains("authentication tag") =>
                    GetErrorMessage("CRYPTO_AUTHENTICATION_TAG_MISMATCH"),
                
                CryptographicException =>
                    GetErrorMessage("CRYPTO_DECRYPTION_FAILED"),
                
                UnauthorizedAccessException =>
                    GetErrorMessage("SYSTEM_PERMISSION_DENIED"),
                
                OutOfMemoryException =>
                    GetErrorMessage("SYSTEM_INSUFFICIENT_MEMORY"),
                
                FileNotFoundException =>
                    GetErrorMessage("AUTH_KEYFILE_NOT_FOUND"),
                
                _ => new ErrorMessageInfo
                {
                    Title = "Error",
                    Message = ex.Message,
                    TroubleshootingSteps = new[]
                    {
                        "Check the application logs for more details",
                        "Try the operation again",
                        "Restart PhantomVault if the issue persists",
                        "Contact support if you need assistance"
                    },
                    Severity = ErrorSeverity.Medium,
                    ErrorCode = "UNKNOWN",
                    TechnicalDetails = ex.ToString()
                }
            };
        }
    }

    public class ErrorMessageInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string[] TroubleshootingSteps { get; set; } = Array.Empty<string>();
        public ErrorSeverity Severity { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string? TechnicalDetails { get; set; }
    }

    public enum ErrorSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
