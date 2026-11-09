export interface Translation {
  appTitle: string;
  studentIdLabel: string;
  studentIdPlaceholder: string;
  studentIdInvalid: string;
  studentIdValid: string;
  studentIdHelp: string;
  demoIdBtn: string;
  navClasses: string;
  navCreateClass: string;
  navMyApplications: string;

  // Classes
  classListTitle: string;
  createClassTitle: string;
  className: string;
  classNamePlaceholder: string;
  totalSeats: string;
  heldSeats: string;
  confirmedSeats: string;
  availableSeats: string;
  applyButton: string;
  viewApplicationButton: string;
  classCreatedSuccess: string;
  submitting: string;
  noClassesYet: string;
  createFirstClass: string;
  seatsStatus: string;
  capacityFull: string;

  // Application
  applicationTitle: string;
  applicationId: string;
  statusLabel: string;
  expiresInLabel: string;
  payFeeButton: string;
  cancelApplicationButton: string;
  confirmCancelPrompt: string;
  confirmedBanner: string;
  expiredBanner: string;
  cancelledBanner: string;
  backToClasses: string;

  // My Applications
  myApplicationsTitle: string;
  noApplications: string;
  viewDetails: string;
  enterIdPrompt: string;
  browseClasses: string;

  // Errors
  errors: {
    ADMIN_KEY_REQUIRED: string;
    ADMIN_KEY_INVALID: string;
    INVALID_STUDENT_ID: string;
    VALIDATION_ERROR: string;
    NOT_FOUND: string;
    NO_SEATS_AVAILABLE: string;
    ALREADY_APPLIED: string;
    HOLD_EXPIRED: string;
    HOLD_CANCELLED: string;
    ALREADY_CONFIRMED: string;
    NOT_YOUR_HOLD: string;
    TEMPORARILY_BUSY: string;
    NETWORK_ERROR: string;
    GENERIC_ERROR: string;
  };
}

export const messages: { en: Translation; bn: Translation } = {
  bn: {
    appTitle: "অনলাইন স্কুল ভর্তি সিস্টেম",
    studentIdLabel: "শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (১৭ সংখ্যা)",
    studentIdPlaceholder: "১৭-সংখ্যার জন্ম নিবন্ধন নম্বর লিখুন (যেমন: 20152691234567890)",
    studentIdInvalid: "জন্ম নিবন্ধন নম্বর অবশ্যই ঠিক ১৭ সংখ্যার হতে হবে।",
    studentIdValid: "✓ সঠিক জন্ম নিবন্ধন নম্বর (১৭ ডিজিট)",
    studentIdHelp: "লগইন লাগবে না। এই ১৭ ডিজিটের নম্বরটি দিয়েই আপনার সব আবেদন চিহ্নিত হবে।",
    demoIdBtn: "ডেমো আইডি বসান",
    navClasses: "ক্লাস তালিকা",
    navCreateClass: "ক্লাস তৈরি (Admin)",
    navMyApplications: "আমার আবেদন",

    // Classes
    classListTitle: "ভর্তি চলছে এমন ক্লাস ও শিফট",
    createClassTitle: "নতুন ক্লাস / শিফট তৈরি করুন",
    className: "ক্লাস ও শিফটের নাম",
    classNamePlaceholder: "যেমন: Class 6 – Morning Shift",
    totalSeats: "মোট সিট",
    heldSeats: "হোল্ড আছে",
    confirmedSeats: "নিশ্চিত",
    availableSeats: "খালি সিট",
    applyButton: "আবেদন করুন",
    viewApplicationButton: "আপনার আবেদন",
    classCreatedSuccess: "ক্লাস সফলভাবে তৈরি হয়েছে!",
    submitting: "প্রক্রিয়াধীন...",
    noClassesYet: "এখনও কোনো ক্লাস বা শিফট তৈরি করা হয়নি।",
    createFirstClass: "প্রথম ক্লাস শিফট তৈরি করুন →",
    seatsStatus: "সিটের অবস্থা",
    capacityFull: "সিট পূর্ণ",

    // Application
    applicationTitle: "ভর্তি আবেদন পত্র",
    applicationId: "আবেদন নম্বর (Hold ID)",
    statusLabel: "বর্তমান অবস্থা",
    expiresInLabel: "মেয়াদ শেষ হতে বাকি",
    payFeeButton: "ভর্তি ফি দিন (Pay Admission Fee)",
    cancelApplicationButton: "আবেদন বাতিল করুন",
    confirmCancelPrompt: "আপনি কি নিশ্চিতভাবে এই আবেদন বাতিল করতে চান? সিটটি সাথে সাথে অন্যদের জন্য উন্মুক্ত হবে।",
    confirmedBanner: "ভর্তি নিশ্চিত হয়েছে! আপনার সিট সফলভাবে সংরক্ষিত।",
    expiredBanner: "আবেদনের মেয়াদ শেষ হয়েছে। সিটটি অন্যদের জন্য উন্মুক্ত করা হয়েছে।",
    cancelledBanner: "আবেদনটি বাতিল করা হয়েছে।",
    backToClasses: "← ক্লাস তালিকায় ফিরুন",

    // My Applications
    myApplicationsTitle: "আমার সব আবেদন",
    noApplications: "এই জন্ম নিবন্ধন নম্বর দিয়ে এখনও কোনো আবেদন করা হয়নি।",
    viewDetails: "বিস্তারিত দেখুন",
    enterIdPrompt: "আপনার আবেদনগুলো দেখতে উপরের বক্সে ১৭-সংখ্যার জন্ম নিবন্ধন নম্বর লিখুন।",
    browseClasses: "আবেদন করার জন্য ক্লাস তালিকা দেখুন →",

    // Errors
    errors: {
      ADMIN_KEY_REQUIRED: "ক্লাস তৈরি করার জন্য অ্যাডমিন সিক্রেট কী প্রয়োজন। অনুগ্রহ করে অ্যাডমিন কী দিন।",
      ADMIN_KEY_INVALID: "প্রদত্ত অ্যাডমিন কী সঠিক নয়। অনুগ্রহ করে সঠিক কী দিন।",
      INVALID_STUDENT_ID: "দয়া করে সঠিক ১৭ সংখ্যার জন্ম নিবন্ধন নম্বর লিখুন।",
      VALIDATION_ERROR: "ইনপুট মানগুলো সঠিকভাবে দিন এবং আবার চেষ্টা করুন।",
      NOT_FOUND: "অনুরোধকৃত ক্লাস বা আবেদনটি পাওয়া যায়নি।",
      NO_SEATS_AVAILABLE: "এই ক্লাসে আর কোনো সিট খালি নেই। সব সিট সংরক্ষিত।",
      ALREADY_APPLIED: "আপনি ইতোমধ্যে এই ক্লাসে আবেদন করেছেন অথবা ভর্তি নিশ্চিত করেছেন।",
      HOLD_EXPIRED: "এই সিট হোল্ডের মেয়াদ শেষ হয়ে গেছে। সিটটি আর সংরক্ষিত নেই।",
      HOLD_CANCELLED: "এই আবেদনটি বাতিল করা হয়েছে।",
      ALREADY_CONFIRMED: "ভর্তি ইতোমধ্যে নিশ্চিত করা হয়েছে।",
      NOT_YOUR_HOLD: "এই আবেদনটি বর্তমান শিক্ষার্থীর সাথে মেলে না।",
      TEMPORARILY_BUSY: "সার্ভারে প্রচুর চাপ রয়েছে। অনুগ্রহ করে কয়েক মুহূর্ত পর আবার চেষ্টা করুন।",
      NETWORK_ERROR: "সার্ভারের সাথে সংযোগ পাওয়া যাচ্ছে না। পুনরায় চেষ্টা করা হচ্ছে...",
      GENERIC_ERROR: "একটি ত্রুটি ঘটেছে। অনুগ্রহ করে আবার চেষ্টা করুন।"
    }
  },
  en: {
    appTitle: "School Admission System",
    studentIdLabel: "Student Birth Registration Number (17 digits)",
    studentIdPlaceholder: "Enter 17-digit birth registration number (e.g. 20152691234567890)",
    studentIdInvalid: "Birth registration number must be exactly 17 digits.",
    studentIdValid: "✓ Valid (17 digits)",
    studentIdHelp: "No login needed. This 17-digit number identifies all your admissions and applications.",
    demoIdBtn: "Fill Demo ID",
    navClasses: "Classes List",
    navCreateClass: "Create Class (Admin)",
    navMyApplications: "My Applications",

    // Classes
    classListTitle: "Available Classes & Shifts",
    createClassTitle: "Create New Class / Shift",
    className: "Class / Shift Name",
    classNamePlaceholder: "e.g. Class 6 – Morning Shift",
    totalSeats: "Total Seats",
    heldSeats: "Held",
    confirmedSeats: "Confirmed",
    availableSeats: "Available",
    applyButton: "Apply for Seat",
    viewApplicationButton: "Your Application",
    classCreatedSuccess: "Class created successfully!",
    submitting: "Submitting...",
    noClassesYet: "No classes or shifts have been created yet.",
    createFirstClass: "Create the first class shift →",
    seatsStatus: "Seats Status",
    capacityFull: "Full",

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
    backToClasses: "← Back to Classes",

    // My Applications
    myApplicationsTitle: "My Applications",
    noApplications: "You have not submitted any applications with this student ID yet.",
    viewDetails: "View Details",
    enterIdPrompt: "Please enter your 17-digit birth registration number in the top bar to view your applications.",
    browseClasses: "Browse available classes to apply →",

    // Errors
    errors: {
      ADMIN_KEY_REQUIRED: "Class creation requires an admin key. Please provide the admin key.",
      ADMIN_KEY_INVALID: "The provided admin key is invalid.",
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

export type Language = 'bn' | 'en';
export type MessageKey = keyof Translation['errors'];

export function getErrorMessage(code?: string, fallbackMessage?: string, lang: Language = 'bn'): string {
  const dict = messages[lang] || messages.bn;
  if (code && code in dict.errors) {
    return dict.errors[code as MessageKey];
  }
  return fallbackMessage || dict.errors.GENERIC_ERROR;
}
