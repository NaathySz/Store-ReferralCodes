# [Store module] Referral Codes
Referral Code module for Store. Allow players to generate their invite code and share with new players for both get credit rewards 

# Config
Config will be auto generated. Default:
```json
{
  "referral_bonus": 100,
  "TopMenuType": 0, //0 - Chat menu, 1 - Kitsune menu
  "KitsuneMenuDeveloperDisplay": true, //If its true, then showing who is developing the Kitsune menu. Its only needed if TopMenuType is 1
  "top_players_limit": 10, //Showing the top X players in the chat menu / Kitsune menu
  "referral_commands": [
    "referral",
    "useinvitecode"
  ],
  "generate_referral_commands": [
    "generate_referral_code",
    "myreferral"
  ],
  "referral_count_commands": [
    "myinvites",
    "invites"
  ],
  "top_referrals_commands": [
    "topreferrals"
  ],
  "bonus_thresholds": {
    "5": 1000,
    "10": 2000,
    "15": 3000
  },
  "database_host": "localhost",
  "database_port": 3306,
  "database_name": "name",
  "database_user": "root",
  "database_password": "password"
}
```
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/L4L611665R)
