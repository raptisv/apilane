---
description: "Configure SMTP and customize the emails Apilane sends for registration confirmation and password recovery."
---

# Email templates

Apilane supports sending emails to users for account-related events such as registration confirmation and password recovery. Each application can be configured with its own SMTP settings and customizable email templates.

---

## SMTP configuration

Before emails can be sent, you must configure the SMTP settings for your application. Open the **Email** tab of your application in the Portal. **Save settings** saves the SMTP settings and the confirmation landing page together.

| Setting | Description |
|---|---|
| **Mail server** | SMTP server hostname (e.g., `smtp.gmail.com`) |
| **Port** | SMTP port — typically `587` (TLS) or `465` (SSL) |
| **Sender address** | The "From" email address shown to recipients |
| **Sender display name** | The display name shown alongside the sender address |
| **User name** | SMTP authentication username |
| **Password** | SMTP authentication password. Once saved it is never shown again; an empty box keeps the stored value |

![Apilane](../assets/email_settings.png)

!!!warning "Required for email features"
    All SMTP fields must be configured for email functionality to work. If any field is missing, email operations will fail with a "Missing application SMTP settings" error.

---

## Available templates

Apilane provides two built-in email templates. Each can be individually enabled or disabled, and both the subject and body can be fully customized. They are listed under **Email templates** on the same tab; editing one shows a live preview of its body.

### Email confirmation

Sent to the user after registration. Typically contains a welcome message and a link to confirm the user's email address.

- **Event code**: `UserRegisterConfirmation`
- **Default subject**: `Welcome!`
- **Default body**: `Thank you for creating an account. Please click on this link to confirm your email address`

### Reset password

Sent to the user when they request a password reset via the `ForgotPassword` API endpoint.

- **Event code**: `UserForgotPassword`
- **Default subject**: `Reset Password`
- **Default body**: `Please click on this link to reset your password`

![Apilane](../assets/email_templates.png)

---

## Template placeholders

Customize your email templates using placeholders. Apilane replaces these with actual values when sending the email.

### User placeholders

These placeholders are available in **all** email templates:

| Placeholder | Description |
|---|---|
| `{Users.ID}` | The user's ID |
| `{Users.Username}` | The user's username |
| `{Users.Email}` | The user's email address |

### Event-specific placeholders

Each email event has its own special placeholder:

| Event | Placeholder | Description |
|---|---|---|
| Email confirmation | `{confirmation_url}` | The URL the user must follow to confirm their email address |
| Reset password | `{reset_password_url}` | The URL the user must follow to reset their password |

### Example template

```html
Hello {Users.Username},

Thank you for joining our platform!

Please click on this <a href="{confirmation_url}">link</a> to confirm 
your email address.

Best regards,
The Team
```

---

## Confirmation flow

When a user registers and email confirmation is enabled:

```mermaid
sequenceDiagram
    participant Client
    participant API as Apilane API
    participant DB as Database
    participant SMTP as Mail Server
    participant User as User Inbox

    Client->>API: POST /Account/Register
    API->>DB: Create user record
    API->>DB: Generate confirmation token
    API->>API: Build confirmation URL<br/>{ServerUrl}/api/Account/Confirm?apptoken=...&token=...
    API->>API: Replace {confirmation_url} in template
    API->>SMTP: Send confirmation email
    SMTP->>User: Deliver email
    API-->>Client: Registration success
    Note over User: User opens email
    User->>API: GET /api/Account/Confirm?apptoken=...&token=...
    API->>DB: Mark email as confirmed
    API-->>User: Redirect to Email confirmation redirect URL
```

1. User registers via `POST /Account/Register`
2. Apilane generates a unique confirmation token
3. The confirmation URL is built as: `{ServerUrl}/api/Account/Confirm?apptoken={token}&token={confirmationToken}`
4. The `{confirmation_url}` placeholder is replaced with this URL in the email template
5. The email is sent to the user
6. When the user clicks the link, their email is marked as confirmed
7. The browser redirects to the **Email confirmation redirect URL** (configurable in [Application settings](application.md#application-settings))

!!!info "Re-sending confirmation"
    If a user loses their initial confirmation email, they can request a new one via the `Email/RequestConfirmation` API endpoint.

---

## Password reset flow

When a user requests a password reset:

```mermaid
sequenceDiagram
    participant Client
    participant API as Apilane API
    participant DB as Database
    participant SMTP as Mail Server
    participant User as User Inbox

    Client->>API: GET /Email/ForgotPassword?email={email}
    API->>DB: Look up user by email
    API->>DB: Generate reset token
    API->>API: Build reset URL<br/>{ServerUrl}/App/{appToken}/Account/Manage/ResetPassword?Token=...
    API->>API: Replace {reset_password_url} in template
    API->>SMTP: Send password reset email
    SMTP->>User: Deliver email
    API-->>Client: OK (always, even if email not found)
    Note over User: User opens email
    User->>API: Follow reset link
    API-->>User: Show password reset form
    User->>API: Submit new password
    API->>DB: Update password
    API-->>User: Password changed confirmation
```

1. Client calls `GET /Email/ForgotPassword?email={email}`
2. Apilane generates a unique reset token
3. The reset URL is built as: `{ServerUrl}/App/{appToken}/Account/Manage/ResetPassword?Token={resetToken}`
4. The `{reset_password_url}` placeholder is replaced in the email template
5. The email is sent to the user
6. The user follows the link to set a new password

The link works for 24 hours. After that the user has to request a new one.

!!!info "Security note"
    Both `RequestConfirmation` and `ForgotPassword` endpoints return success even if the email does not exist in the system. This prevents email enumeration attacks.

---

## Confirmation landing page

After a user confirms their email by following the URL provided in the email, the browser eventually lands on a page. By default this is a page of the Portal (`{PortalUrl}/account/email-confirmed`, where `PortalUrl` is the API's setting). To use a URL of your choice, set **Redirect URL** under 'Email confirmation landing page' on the **Email** tab.

![Apilane](../assets/email_confirm_land.png)
