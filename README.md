# TAT (Time & Attandance Tracking)

This system tracks the employee clock in and out of a location.

Location information will saved in firebase database

3 roles 
Admin, Team Lead and Employee


Admin Responsiabilities
1. As an admin I can CRUD locations
2. As an admin I can CRUD employees
3. As an admin I have access to reports.
4. As an admin I can update the time.
5. As an admin I can do everything that a Team Lead and Employee can do
5. As an admin, I have accesss to settings page.
6. The report has to show correct time for the number of hours an employee worked.


Team Lead Responsiabilities
1. As an teamlead, I clock in and out like a normal emplyee. 
2. As an teamlead, I create a schedule for the day for exmaple at what time they take break  or lunch etc. It is expected that employees will follow that schedule. There should be an option to print the schedule something like google calander to see what time they take  breaks and lunches etc.
3. As an teamlead, I should be able to preview the schedule in advance and make changes accordingly.
4. If anyone is absent or don't show up for their shift. Admin should be able to add a note for that employee. This will be visible to employees in their respective reports.
5. As a teamlead, I am able to see if the calander view of breaks and lunches time in and time out entries as well. 


Employee Responsiabilities
1. As an employee, I clock in and out of the system everytime I am on the shift
2. Date and time entry is default from settings


Settings
1. timezone selection, default is Pacific timezone.
2. Date & time format default MM/dd/yyyy hh:mm:ss
3. Allow manual date and time change Yes/No. default is no.
4. breaks enabled Yes/No. 
   YES :  how many minutes.
          a setting to ask if break time is counted in hours. If an employee
   NO : No Action

5. Lunch Needed Yes/No. If Yes how many minutes