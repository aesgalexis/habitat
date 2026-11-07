# Legacy contact adapters

The vCard parser and Google Contacts reader are retained only in the Contacts
checks. The current desktop flow creates manual contacts and optionally syncs
them with Drive. Legacy address book formats, import transformations, merging
and browser protocol compatibility remain covered without exposing retired UI
or shipping unused provider readers in Habitat.Core.
