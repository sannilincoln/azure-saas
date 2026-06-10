#!/usr/bin/env python3

import json
import sys
import re

def get_b2c_value(
    config: dict,
    key: str,
    keyName: str) -> 'dict[str, dict[str, str]]':

    value = config['azureb2c'][key]

    return {
            keyName: {
                'value': value
            }
        }

def get_claimTransformer_value(
    config: dict,
    key: str,
    keyName: str) -> 'dict[str, dict[str, str]]':

    value = config['claimToRoleTransformer'][key]
    return {
            keyName: {
                'value': value
            }
        }

def get_deploy_b2c_value(
    config: dict,
    key: str,
    keyName: str) -> 'dict[str, dict[str, str]]':

    value = config['deployment']['azureb2c'][key]
    return {
            keyName: {
                'value': value
            }
        }

def get_app_value(
    config: dict, 
    app_name: str, 
    key: str,
    keyName: str) -> 'dict[str, dict[str, str]]':
    
    for item in config['appRegistrations']: 
        if item['name'] == app_name: 
            return {
                keyName: {
                    'value': item[key]
                }
            }

def get_output_value(outputs: dict, output_name: str) -> 'dict[str, dict[str, str]]':
    item = outputs[output_name]
    if item : return {
        output_name : {
            'value': item['value']
        }
    }

def patch_paramenters_file(
        app_name: str, 
        identity_outputs: str, 
        paramenter_file: str,
        config_file: str) -> None:

    with open(config_file, 'r') as f:
        config = json.load(f)

    with open(identity_outputs, 'r') as f:
        identity_outputs = json.load(f)

    with open(paramenter_file, 'r') as f:
        parameters = json.load(f)

    parameters['parameters'].update(get_output_value(identity_outputs, 'version'))
    parameters['parameters'].update(get_output_value(identity_outputs, 'keyVaultName'))
    parameters['parameters'].update(get_output_value(identity_outputs, 'keyVaultUri'))

    parameters['parameters'].update(get_output_value(identity_outputs, 'userAssignedIdentityName'))
    parameters['parameters'].update(get_output_value(identity_outputs, 'appConfigurationName'))
    
    # Workforce multitenant (Phase H): every customer signs in from their own Entra (Azure AD)
    # tenant, so the authority is the shared multitenant endpoint and TenantId is 'organizations'.
    # Domain is the publisher's home tenant (used by the Permissions Graph lookup). These are
    # literals now, which also decouples config generation from B2C/IEF provisioning.
    parameters['parameters'].update({'azureB2CDomain': {'value': config['initConfig']['entraExternalId']['tenantDomain']}})
    parameters['parameters'].update({'azureB2cTenantId': {'value': 'organizations'}})
    parameters['parameters'].update({'azureAdB2CInstanceURL': {'value': 'https://login.microsoftonline.com/'}})
    
    parameters['parameters'].update(get_b2c_value(config, 'signedOutCallBackPath', 'signedOutCallBackPath'))

    parameters['parameters'].update(get_app_value(config, app_name, 'appId', 'clientId'))
    parameters['parameters'].update(get_app_value(config, app_name, 'certificateKeyName', 'certificateKeyName'))

    parameters['parameters'].update(get_claimTransformer_value(config, 'authenticationType', 'authenticationType'))
    parameters['parameters'].update(get_claimTransformer_value(config, 'roleClaimType', 'roleClaimType'))
    parameters['parameters'].update(get_claimTransformer_value(config, 'sourceClaimType', 'sourceClaimType'))

    with open(paramenter_file, 'w+') as f:
        f.write(json.dumps(parameters, indent=4))

# Main entry point for the script 
if __name__ == "__main__":
    app_name = sys.argv[1]
    identity_outputs = sys.argv[2]
    paramenter_file = sys.argv[3]
    config_file = sys.argv[4]

    patch_paramenters_file(app_name, identity_outputs, paramenter_file, config_file)