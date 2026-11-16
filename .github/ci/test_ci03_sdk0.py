"""Inert SDK0 data-boundary cases; not producer144/96 or native proof."""
import json,sys
from pathlib import Path
from typed_host_03 import Denied,consume_after_checks
corpus=json.loads((Path(__file__).with_name('ci03-sdk0-fixtures.json')).read_text())
rows=[]
for case in corpus['cases']:
    calls=[0]
    def spy(decision):calls[0]+=1;return 'ALLOW_DATA_ONLY'
    try:observed=consume_after_checks(**case['input'],fixed_consumer=spy)
    except Denied as error:observed=str(error)
    if observed!=case['expected'] or calls[0]!=case['backendSpyCalls']:raise RuntimeError(case['id'])
    rows.append({'case':case['id'],'observed':observed,'backendSpyCalls':calls[0]})
print(json.dumps({'scope':'SDK0_DATA_BOUNDARIES_ONLY','actualOptimize':sys.flags.optimize,'cases':rows,'realSDKExecution':False}))
