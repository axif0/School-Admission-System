export const messages = {
  en: {
    appTitle: "School Admission System",
    studentIdLabel: "Student Birth Registration Number (17 digits)",
    studentIdPlaceholder: "Enter 17-digit birth registration number (e.g. 20152691234567890)",
    studentIdInvalid: "Birth registration number must be exactly 17 digits.",
    studentIdHelp: "Your 17-digit student number identifies all your admissions and applications.",
    navClasses: "Available Classes",
    navCreateClass: "Create Class (Admin)",
    navMyApplications: "My Applications",
    
    // Classes
    classListTitle: "Available Classes & Shifts",
    createClassTitle: "Create New Class / Shift",
    className: "Class / Shift Name",
    classNamePlaceholder: "e.g. Class 6 - Morning Shift",
    totalSeats: "Total Seats",
    heldSeats: "Held",
    confirmedSeats: "Confirmed",
    availableSeats: "Available",
    applyButton: "Apply for Seat",
    viewApplicationButton: "Your Application",
    classCreatedSuccess: "Class created successfully!",
    submitting: "Submitting...",
    
    // Application
    applicationTitle: "Admission Application",
    applicationId: "Application ID",
    statusLabel: "Status",
    expiresInLabel: "Time Remaining to Pay Fee",
    payFeeButton: "Pay Admission Fee",
    cancelApplicationButton: "Cancel Application",
    confirmCancelPrompt: "Are you sure you want to cancel this application? The held seat will be immediately released.",
    confirmedBanner: "Admission Confirmed! Your seat has been secured.",
    expiredBanner: "Hold Expired. This seat has been released for other applicants.",
    cancelledBanner: "Application Cancelled.",
    
    // My Applications
    myApplicationsTitle: "My Applications",
    noApplications: "You have not submitted any applications with this student ID yet.",
    viewDetails: "View Details",
    
    // Errors
    errors: {
      INVALID_STUDENT_ID: "Please enter a valid 17-digit birth registration number.",
      VALIDATION_ERROR: "Please check your input values and try again.",
      NOT_FOUND: "The requested class or application was not found.",
      NO_SEATS_AVAILABLE: "All seats in this class are currently held or confirmed. No seats available.",
      ALREADY_APPLIED: "You already have an active hold or confirmed admission in this class.",
      HOLD_EXPIRED: "This seat hold has expired. The seat is no longer reserved.",
      HOLD_CANCELLED: "This application has been cancelled.",
      ALREADY_CONFIRMED: "This admission is already confirmed.",
      NOT_YOUR_HOLD: "This application does not belong to the currently entered student ID.",
      TEMPORARILY_BUSY: "The system is currently handling high traffic. Please try again in a few moments.",
      NETWORK_ERROR: "Connection problem. Unable to reach the server. Retrying...",
      GENERIC_ERROR: "Something went wrong. Please try again."
    }
  }
};

export type MessageKey = keyof typeof messages.en.errors;

export function getErrorMessage(code?: string, fallbackMessage?: string): string {
  if (code && code in messages.en.errors) {
    return messages.en.errors[code as MessageKey];
  }
  return fallbackMessage || messages.en.errors.GENERIC_ERROR;
}
