namespace BusinessCentral.LinterCop.Setup;

table 50108 "My Table"
{
    fields
    {
        field(1; MyField; Code[20]) { }

        field(2; [|"My TableRelation Field"|]; Code[10])
        {
            TableRelation = BusinessCentral.LinterCop.Setup."My Other Table"."My Field";
        }
    }

    keys
    {
        key(PK; MyField) { }
    }
}


table 50101 "My Other Table"
{
    fields
    {
        field(1; "My Field"; Code[20]) { }
    }
}
