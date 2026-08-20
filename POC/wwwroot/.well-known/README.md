# Apple Pay domain association

Apple Pay on the web only works once the domain serving this site is registered with Stripe.

1. Stripe Dashboard -> Settings -> Payments -> Payment methods -> Apple Pay -> Add a new domain.
2. Download `apple-developer-merchantid-domain-association` and drop it in this folder
   (no file extension, no edits).
3. Redeploy, then confirm `https://<your-domain>/.well-known/apple-developer-merchantid-domain-association`
   returns the file contents over HTTPS.
4. Click Verify in the Stripe Dashboard.

`GET /api/repayment/config` reports `isApplePayDomainFileConfigured` so the POC can tell you
whether this step has been done.

Google Pay needs no equivalent file for a Stripe-hosted web integration.
