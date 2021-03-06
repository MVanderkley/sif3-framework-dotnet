﻿/*
 * Copyright 2016 Systemic Pty Ltd
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Sif.Framework.Consumers;
using Sif.Framework.Demo.Uk.Consumer.Models;
using Environment = Sif.Framework.Model.Infrastructure.Environment;

namespace Sif.Framework.Demo.Uk.Consumer.Consumers
{

    class LearnerPersonalConsumer : BasicConsumer<LearnerPersonal>
    {

        public LearnerPersonalConsumer(string applicationKey, string instanceId = null, string userToken = null, string solutionId = null)
            : base(applicationKey, instanceId, userToken, solutionId)
        {
        }

        public LearnerPersonalConsumer(Environment environment)
            : base(environment)
        {
        }

    }

}
