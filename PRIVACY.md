# Job Hunter — privacy policy

Job Hunter is a personal, single-user Telegram assistant for a job search. It is not offered to the public:
it only works for the owner of the server it runs on, and it answers only the owner's Telegram account.

## What it reads

- **Gmail** (`gmail.readonly`): only messages matching a fixed search — e-mails from Djinni (`magic@djinni.co`)
  and LinkedIn job alerts from the last few days. It cannot send, change or delete e-mail.
- Public vacancy pages and RSS feeds of job boards.

## What it stores and where

Parsed vacancies (title, company, link, description), recruiter messages that belong to an application,
and the owner's own profile and CV are stored in a private SQLite database on the owner's server.
Nothing is published, sold or shared with third parties.

## Processing by OpenAI

To score a vacancy or draft a reply, the vacancy text, the recruiter's message and the owner's profile are sent to
the OpenAI API. No other e-mail content is sent.

## Revoking access

Remove "Job Hunter" at <https://myaccount.google.com/permissions>. The bot stops reading mail immediately.

## Contact

makspalij1@gmail.com
